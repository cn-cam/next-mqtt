# 集群

NextMQTT 集群是无中心网状结构。每个节点拥有自己的会话，并按订阅过滤器转发消息。

## 配置

```json
{
  "Cluster": {
    "Enabled": true,
    "NodeId": "node-a",
    "AdvertiseUrl": "http://node-a:8080",
    "Seeds": ["http://node-b:8080", "http://node-c:8080"],
    "Secret": "至少 16 字符的共享密钥",
    "HeartbeatSeconds": 5,
    "NodeTimeoutSeconds": 20
  }
}
```

环境变量：

```bash
NEXTMQTT_CLUSTER__ENABLED=true
NEXTMQTT_CLUSTER__NODEID=node-a
NEXTMQTT_CLUSTER__ADVERTISEURL=http://node-a:8080
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
NEXTMQTT_CLUSTER__SECRET=shared-secret-at-least-16-chars
```

要求：节点间 8080 可达、时钟偏差小于 5 分钟、NodeId 唯一、密钥一致。

## 成员发现

- 每个节点周期性发送心跳（节点 ID、地址、普通订阅过滤器）
- 接收方合并成员并返回自己的成员表
- 新节点只需一个可达种子，即可通过 gossip 发现其余节点
- 超过超时变为 suspect，3 倍超时后移除

控制台“集群”页与 `GET /api/v1/cluster` 可查看当前视图。

## 自愈

- **故障节点会被摘除**：超过成员超时标记为 suspect，3 倍超时后从成员表移除，它的过滤器立刻停止吸引流量。
- **限流立即重算**：节点加入或离开会马上重新分摊连接、发布与主题配额，不必等下一轮清扫；集群总量保持不变。
- **运行期加入**：重启或新增节点只需一个可达种子，从第一个心跳响应里拿到成员表，其余节点也以同样方式认识它。
- **保留消息补齐**：新节点首次加入时向一个对端拉取保留消息集合，保留原始时间戳，因此仍按 last-write-wins 合并。
- **会话副本防崩溃丢失**：每 `SessionReplicaSeconds` 秒把持久会话快照推给对端。原节点崩溃后，客户端在别的节点重连会使用最新副本；但只在副本记录的属主已不可用时才采用，避免复活仍在别处运行的会话。

会话跟随客户端：`CleanStart=false` 重连到另一节点时会话会被接管，订阅与离线队列随之迁移；
`CleanStart=true` 仍然从空开始。旧连接以 0x8E 关闭，迁移不发布遗嘱。

## 可用性模型

前提是「同一时刻一个客户端只连一个节点，断线后由客户端自行重连」。在这个前提下：

| 故障 | 影响 | 集群的处理 |
| --- | --- | --- |
| 客户端 TCP 断开 | 会话转为离线 | 队列继续接收消息，直到过期时间结束 |
| 节点崩溃 | 它持有的会话不可达，路由立即停止向它转发 | 超过 `NodeTimeoutSeconds` 标记可疑，3 倍超时后摘除；客户端重连到别处后会话恢复 |
| 客户端换节点重连 | 会话在原节点 | 新节点先向原属主索要；属主不可用或答复「没有该会话」时使用最新副本 |
| 属主磁盘丢失并重启 | 本地会话文件为空 | 用对端副本恢复；即使属主还活着，只要它答复没有该会话就允许回退 |
| 全集群重启 | 内存清空 | 各节点重载自己的会话文件与副本文件，按 `(lastSeen, nodeId)` 选最新副本 |
| 网络分区 | 两边成员视图不同 | 只有持有真实连接的一侧服务该客户端；副本记录的属主在本地视图里仍存活时绝不使用副本 |
| 副本推送延迟 | 副本最多滞后 `SessionReplicaSeconds` | 恢复后可能缺最后几秒的排队消息 |

**恢复点（RPO）**：副本最多滞后 5 秒；属主自己的会话快照最多滞后 30 秒；保留消息每次写入都落盘。
已投递但未确认的消息会从队列重放，可能重复一次，QoS 1/2 允许。

**恢复时间（RTO）**：一次重连加一次内部往返，不需要人工干预。

**明确的边界**：出站 inflight 确认状态不迁移（从队列重放，可能重复到达，QoS 1/2 允许）；
等待 PUBREL 的入站 QoS 2 发布随会话迁移，客户端可以在新节点直接完成握手。
过期时间为 0 的会话不复制（它本来就不跨连接）；跨节点不保证顺序，也不保证 exactly-once。

**脑裂取舍**：归属靠证据而不是共识——活着的属主优先；属主答复没有该会话时用最新副本；
两份恢复副本用 `(lastSeen, nodeId)` 决出胜负，因此各节点无需协调也能得到一致结论。
代价是分区期间可能出现两个可接受同一客户端 ID 的副本，但客户端本身只能连一处，MQTT 语义可以接受。

## 路由

1. 发布先在本地投递
2. 集群开启时：
   - retain 发布复制到所有存活节点
   - 普通发布只转发给广告了匹配过滤器的节点
3. 远端校验 HMAC、按消息 ID 去重，再走本地路由，不再二次转发

```text
publisher ──► node-a ──► 本地订阅者
                  ├── 签名 POST ──► node-b
                  └── 签名 POST ──► node-c
```

## 投递语义

- **单链路尽力而为**：跨节点是 HTTP 转发，节点故障可能丢失该路径消息
- **去重**：有界“已见消息”缓存
- **会话归属**：会话固定在接入节点，换节点重连会新建会话
- **共享订阅跨节点**：节点在心跳里广告 `$share` 组，每条消息按（消息 ID + 组名）哈希选中唯一节点投递，不会在多个节点各投一次
- **过滤器最终一致**：新订阅最多等待一个心跳周期

## 安全

内部 API 使用 HMAC-SHA256：
`X-NextMQTT-Timestamp` + `X-NextMQTT-Signature`，签名内容为
`timestamp.body`，允许 5 分钟时钟偏差。

- 密钥不随请求传输
- 跨不可信网络时使用 HTTPS AdvertiseUrl
- 在入口或 NetworkPolicy 中阻止 `/internal/cluster/*`
- 轮换密钥需要重启节点，暂不支持滚动轮换

控制台登录与集群密钥相互独立。未设置 `NEXTMQTT_ADMIN_TOKEN_SECRET` 时，每个节点
用自己的临时密钥签发 Bearer Token，因此一个节点签发的 Token 无法在另一个节点
使用。若希望只登录一次即可访问任意节点的控制台或 API，请在所有节点设置相同的
值。

## Kubernetes

StatefulSet 使用 headless service 与 Pod DNS：

```text
NEXTMQTT_CLUSTER__NODEID=$(POD_NAME)
NEXTMQTT_CLUSTER__ADVERTISEURL=http://$(POD_NAME).nextmqtt-headless.nextmqtt.svc.cluster.local:8080
```

种子为前三个 Pod。扩容后的新 Pod 通过种子加入并由 gossip 传播。

## 验证

```bash
curl -s http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN" | jq '.nodes'

nextmqtt sub --port 1884 --topic cluster/# --count 1 &
nextmqtt pub --port 1883 --topic cluster/test --message hello --qos 1
```

订阅后至少等待一个心跳再发布。

## 排错

| 现象 | 检查 |
| --- | --- |
| 只有本节点 | AdvertiseUrl、8080 防火墙、种子拼写 |
| 401 | 密钥不一致或时钟偏差 |
| 节点反复 alive/dead | 心跳与超时配置、CPU |
| 远端缺少 retain | 发布时对端离线，需重新发布 |
| 远端订阅漏消息 | 过滤器尚未传播，等待心跳 |
| 重复投递 | 去重缓存被清理或时钟异常 |

## 限制

- 会话接管覆盖订阅、离线队列，以及仍在等待 PUBREL 的入站 QoS 2 发布；出站 inflight
  确认状态不迁移，消息会从队列重放，客户端可能看到一次重复
- 副本最多滞后 `SessionReplicaSeconds`，且只存在对端内存里；所有其他节点同时重启时副本也会丢
- 每次心跳全量过滤器，默认上限 10,000
