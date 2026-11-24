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
| `nextmqtt_authentication_failures_total` | counter | 认证失败 |
| `nextmqtt_publishes_received_total` | counter | 接收发布 |
| `nextmqtt_publishes_sent_total` | counter | 投递发布 |
| `nextmqtt_bytes_received_total` | counter | 入站字节 |
| `nextmqtt_bytes_sent_total` | counter | 出站字节 |
| `nextmqtt_messages_dropped_total` | counter | 丢弃消息 |
| `nextmqtt_retransmissions_total` | counter | QoS 重传 |
| `nextmqtt_clients_connected` | gauge | 在线连接 |
| `nextmqtt_sessions` | gauge | 会话数 |
| `nextmqtt_subscriptions` | gauge | 订阅数 |
| `nextmqtt_retained_messages` | gauge | 保留消息数 |
| `nextmqtt_cluster_nodes` | gauge | 集群节点数 |

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

内存主要由会话、离线队列、保留消息和 inflight 窗口占用。

## 会话生命周期

- 持久会话在过期前保留在内存
- 维护循环每 `SweepInterval`（默认 30 秒）检查过期
- `cleanStart=true` 会在下次连接时丢弃旧会话
- 控制台“断开”发送 `0x98`，之后按正常过期规则处理

## 升级

1. 逐个节点 drain 或缩容
2. 客户端重连并重新订阅（当前状态在内存）
3. 更新镜像标签
4. 观察 `/readyz` 与连接指标
5. 集群至少保留两个节点

滚动重启会丢失队列和保留消息，持久化能力在路线图中。

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
