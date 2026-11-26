# 运维

## 健康检查

| 端点 | 用途 |
| --- | --- |
| `/healthz` | 存活检查 |
| `/readyz` | 就绪检查 |
| `/api/v1/health` | API 健康与版本 |
| `/api/v1/ready` | API 就绪与会话数 |

健康端点不鉴权，不要在其中放入敏感信息。

## Prometheus 指标

`GET /metrics` 输出 Prometheus 文本格式。默认无需鉴权；可设置
`Admin:MetricsRequireAuth=true`。

| 指标 | 类型 | 说明 |
| --- | --- | --- |
| `nextmqtt_connections_opened_total` | counter | 接受连接数 |
| `nextmqtt_connections_closed_total` | counter | 关闭连接数 |
| `nextmqtt_connections_rejected_total` | counter | CONNECT 阶段拒绝 |
| `nextmqtt_authentication_failures_total` | counter | 认证失败 |
| `nextmqtt_publishes_received_total` | counter | 接收发布 |
| `nextmqtt_publishes_sent_total` | counter | 投递发布 |
| `nextmqtt_bytes_received_total` | counter | 入站字节 |
| `nextmqtt_bytes_sent_total` | counter | 出站字节 |
| `nextmqtt_messages_dropped_total` | counter | 丢弃消息 |
| `nextmqtt_retransmissions_total` | counter | QoS 重传 |
| `nextmqtt_subscriptions_created_total` | counter | 成功订阅 |
| `nextmqtt_sessions_expired_total` | counter | 过期删除的会话 |
| `nextmqtt_connections_rate_limited_total` | counter | 连接限流拒绝 |
| `nextmqtt_publishes_rate_limited_total` | counter | 发布限流丢弃 |
| `nextmqtt_clients_connected` | gauge | 在线连接 |
| `nextmqtt_sessions` | gauge | 会话数 |
| `nextmqtt_subscriptions` | gauge | 订阅数 |
| `nextmqtt_retained_messages` | gauge | 保留消息数 |
| `nextmqtt_cluster_nodes` | gauge | 集群节点数 |
| `nextmqtt_build_info` | gauge | 版本标签 |

## 控制台

概览页每 3 秒轮询一次 API，显示连接/会话数、订阅与保留消息、最近一分钟发布吞吐、
连接/认证/丢弃/重传计数以及集群成员。客户端、订阅与保留消息表每 5 秒刷新并在服务端
分页。发布控制台使用与 REST API 相同的无 ACL 管理路径。

MQTT 调试器（Messaging → Debugger）由浏览器经 WebSocket 直连 Broker，验证真实协议
而非管理 API。支持 MQTT 3.1.1 / 5.0、QoS 0–2、保留消息、遗嘱、MQTT 5 会话与发布
属性、带 noLocal 的订阅、retain-as-published、retain handling、PING、报文跟踪与
JSON 日志导出。它使用普通 MQTT 凭据，同样受认证与 ACL 约束。

## 日志

- 输出到 stdout
- 包含监听器启动、连接/断开、集群加入/超时、启动密码警告
- `Logging:LogLevel:NextMqtt=Debug` 可查看协议级错误
- 默认不记录负载内容

## 容量

| 场景 | CPU | 内存 |
| --- | --- | --- |
| 1 万空闲连接 | 0.5 核 | 256–512 MiB |
| 1 万 QoS 1 活跃连接 | 1–2 核 | 512 MiB–1 GiB |
| 大保留集/离线队列 | 1+ 核 | 按负载估算 |

内存主要由会话、离线队列、保留消息和 inflight 窗口占用。内存紧张时降低队列上限或
会话过期时间。当前版本没有保留消息总量配额，请监控 `nextmqtt_retained_messages` 与
负载大小。

### 实测参考数据

| 场景 | 结果 |
| --- | --- |
| 1,000 并发连接 | 1.09 秒建连完成（≈920 连接/秒） |
| 5,000 并发连接 | 3.16 秒建连完成（≈1,580 连接/秒），0 失败 |
| QoS 0，50 客户端 × 2,000 消息，64 B | ≈194,000 消息/秒 |
| QoS 0，100 客户端 × 2,000 消息，128 B | ≈186,000 消息/秒 |
| QoS 1，20 客户端 × 500 消息 | ≈19,800 消息/秒 |

测试环境：单机 Windows 11、Intel Core i7-11700F（8 核 16 线程）、32 GiB 内存，
Broker 与压测客户端共用同一台机器经 loopback 通信。数据偏保守：独立压测机、
Linux 和专用主机上会更高。可用 `nextmqtt bench` 与 `nextmqtt bench-connections`
复现；启用 TLS、大量保留消息或大扇出都会降低吞吐。

在 Docker Desktop 上通过端口映射运行时，本机端口转发大约在 2,000 个并发连接处饱和，
超过后连接在到达 Broker 前就会失败。要测真实建连能力，请把压测程序放进容器网络，
或使用 Linux 主机。

## 会话生命周期

- 持久会话在过期前保留在内存；启用 `Storage:Session:Mode=File` 后也会写入磁盘快照
- 重连时由新节点接管；属主已死时使用最新副本（副本同样可落盘）
- 维护循环每 `SweepInterval`（默认 30 秒）检查过期
- `cleanStart=true` 会在下次连接时丢弃旧会话
- 控制台“断开”发送 `0x98`，之后按正常过期规则处理

## 升级

1. 逐个节点 drain 或缩容
2. 需要保留状态时挂载会话、副本与保留消息文件；否则准备客户端重连并重新订阅
3. 更新镜像标签
4. 观察 `/readyz` 与连接指标
5. 集群至少保留两个节点

挂载文件存储且副本足够新时，滚动重启会保留持久会话；否则会丢失队列和保留消息。
保留消息只是最后已知值存储，不是持久事件日志。

## 排错

| 现象 | 可能原因 | 处理 |
| --- | --- | --- |
| 控制台 401 | 令牌过期或临时密钥重启 | 重新登录；设置固定 Token Secret |
| MQTT 全部被拒 | 关闭匿名且无用户/JWT | 配置用户或 JWT |
| SUBACK `0x87` | ACL 拒绝过滤器 | 检查 ACL 覆盖 |
| PUBACK `0x87` | ACL 拒绝主题 | 检查发布规则 |
| 空闲断连 | 超过 keep-alive | 调整客户端与网络超时 |
| 离线消息丢失 | 非持久会话或队列满 | 设置会话过期、提高队列 |
| 节点 suspect/dead | 网络/URL/密钥/时钟 | 检查 8080、AdvertiseUrl、密钥 |
| 丢弃计数上升 | 慢消费者/队列小 | 观察客户端吞吐，谨慎调大 |

## 备份

`Storage:Retained:Mode` 为 `file` 时，`Storage:Retained:Path` 的日志保存全部保留
消息，可在运行中复制（追加写，压缩使用原子改名）。`Storage:Session:Mode` 为 `file`
时会话快照写入 `Storage:Session:Path`；`Storage:Replicas:Mode` 为 `file` 时收到的副本
写入 `Storage:Replicas:Path`。复制这些文件时应停止对应节点，或改用下面的 API。

与存储模式无关的逻辑备份：

```bash
curl -s http://localhost:8080/api/v1/backup -H "Authorization: Bearer $TOKEN" \
  > nextmqtt-backup.json
curl -s -X POST http://localhost:8080/api/v1/backup -H "Authorization: Bearer $TOKEN" \
  -H 'content-type: application/json' --data @nextmqtt-backup.json
```

导入会替换保留消息，并补入当前不在线的持久会话。出站 inflight 确认不在备份中，等待
PUBREL 的入站 QoS 2 会包含。

配置保存在环境变量与 Kubernetes/Compose 文件中，也需要备份。管理令牌密钥与集群密钥
必须保留，否则会导致令牌失效或节点信任中断。
