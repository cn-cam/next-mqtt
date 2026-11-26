# 集群

NextMQTT 集群是无协调者的网状结构。每个节点拥有自己的会话，并把发布转发给广告了
匹配订阅的对端。

## 前提

- 至少 16 个字符的共享 HMAC 密钥。
- 每个节点有一个可达的 `AdvertiseUrl`（对端通过 HTTP 或 HTTPS 调用）。
- 节点之间开放 TCP 8080。
- 时钟同步在五分钟内（签名请求的时间戳有五分钟窗口）。
- 唯一的 `NodeId`。

## 配置

```json
{
  "Cluster": {
    "Enabled": true,
    "NodeId": "node-a",
    "AdvertiseUrl": "http://node-a:8080",
    "Seeds": ["http://node-b:8080", "http://node-c:8080"],
    "Secret": "shared-secret-at-least-16-chars",
    "HeartbeatSeconds": 5,
    "NodeTimeoutSeconds": 20,
    "RequestTimeoutSeconds": 5,
    "MaxFiltersPerHeartbeat": 10000
  }
}
```

环境变量形式：

```bash
NEXTMQTT_CLUSTER__ENABLED=true
NEXTMQTT_CLUSTER__NODEID=node-a
NEXTMQTT_CLUSTER__ADVERTISEURL=http://node-a:8080
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
NEXTMQTT_CLUSTER__SEEDS__1=http://node-c:8080
NEXTMQTT_CLUSTER__SECRET=shared-secret-at-least-16-chars
```

一个节点只需要一个可达种子。成员关系通过 gossip 传播，新节点会自动出现在整个网状
结构中。

## 成员管理

- 每个节点启动后立即开始心跳循环。
- 心跳包含节点 ID、Advertise URL 与普通订阅过滤器。
- 接收方把发送方合并进成员表，并回复自己的成员表。
- 收到的成员也会被合并，成员关系由此传遍全网。
- 超过 `NodeTimeoutSeconds` 未响应的节点先变为 `suspect`，再变为 `dead`，3 倍超时后
  被移除。

控制台的 **Cluster** 页面与 `GET /api/v1/cluster` 展示当前视图。

## 自愈

- **移除死节点。** 静默超过 `NodeTimeoutSeconds` 的对端显示为 `suspect`，随后 `dead`，
  三次超时后从表中删除。它广告的过滤器立即停止接收转发，崩溃的节点不会继续吸引流量。
- **限额立即重算。** 节点加入或离开时，连接、发布与主题配额的每节点份额立即重新计算，
  不必等待下一轮巡检。集群总量保持配置值。
- **运行中加入是常态。** 重启或新增节点只需要一个可达种子。它从第一次心跳响应合并
  成员表，其他节点也以同样方式得知它。
- **保留消息追平。** 首次加入时节点通过签名内部 API 从一个对端拉取保留集合，保留原始
  时间戳，因此 last-write-wins 仍然生效。离线期间发布的消息不会丢失。
- **会话副本抗崩溃。** 每 `SessionReplicaSeconds` 秒把持久会话快照推给对端。属主死亡
  且客户端在别处重连时，新节点在属主可达时向其索要会话，否则回退到最新副本。只有当
  属主已不存活、索要时无法访问，或活着的属主答复自己没有该会话（例如自身文件丢失）
  时才使用副本，因此过期副本不会复活仍在别处运行的会话。

会话跟随客户端：`CleanStart=false` 重连到另一节点时会话会被接管，离线队列与订阅随之
迁移；`CleanStart=true` 仍然从空开始。

## 可用性模型

前提是「同一时刻一个客户端只连一个节点，断线后由客户端自行重连」。在这个前提下：

| 故障 | 影响 | 集群的处理 |
| --- | --- | --- |
| 客户端 TCP 断开 | 会话转为离线 | 队列继续接收消息，直到过期时间结束 |
| 节点崩溃 | 它持有的会话不可达，路由立即停止向它转发 | 超过 `NodeTimeoutSeconds` 标记 dead；其订阅者的消息在客户端重连并接管前会丢失 |
| 客户端换节点重连 | 会话在原节点 | 新节点先向原属主索要；属主不可用、无法访问或答复「没有该会话」时使用最新副本 |
| 属主磁盘丢失并重启 | 本地会话文件为空 | 用对端副本恢复；即使属主还活着，只要它答复没有该会话就允许回退 |
| 全集群重启 | 内存清空 | 各节点重载自己的会话文件与副本文件，按 `(lastSeen, nodeId)` 选最新副本 |
| 网络分区 | 两边成员视图不同 | 只有持有真实连接的一侧服务该客户端；副本记录的属主在本地视图里仍存活时绝不使用副本 |
| 副本推送延迟 | 副本最多滞后 `SessionReplicaSeconds` | 恢复后可能缺最后几秒的排队消息 |

**恢复点（RPO）**：副本最多滞后 5 秒；属主自己的会话快照最多滞后 30 秒；保留消息每次
写入都落盘。已投递但未确认的消息会从队列重放，可能重复一次，QoS 1/2 允许。

**恢复时间（RTO）**：一次重连加一次内部往返，不需要人工干预。

**明确的边界**：出站 inflight 确认状态不迁移（从队列重放，可能重复到达，QoS 1/2 允许）；
等待 PUBREL 的入站 QoS 2 发布随会话迁移，客户端可以在新节点直接完成握手。过期时间为
0 的会话不复制（它本来就不跨连接）；跨节点不保证顺序，也不保证 exactly-once。

**脑裂取舍**：归属靠证据而不是共识——活着的属主优先；属主答复没有该会话时用最新副本；
两份恢复副本用 `(lastSeen, nodeId)` 决出胜负，因此各节点无需协调也能得到一致结论。
代价是分区期间可能出现两个可接受同一客户端 ID 的副本，但客户端本身只能连一处，MQTT
语义可以接受。

## 路由

1. 发布先在本地投递。
2. 集群开启时，路由器选择存活对端：
   - retain 发布发往所有存活对端，使保留状态收敛；
   - 其他发布只发给广告了匹配过滤器的对端。
3. 对端校验 HMAC 签名、按消息 ID 去重，然后调用本地路由路径，不再转发。

```text
publisher ──► node-a ──► 本地订阅者
                  │
                  ├── 签名 POST /internal/cluster/publish ──► node-b
                  └── 签名 POST /internal/cluster/publish ──► node-c
```

慢或不可达的对端不会阻塞本地投递：每次转发有自己的请求超时，失败只记 debug 日志。

## 保留消息

retain 发布复制到所有存活节点。任何节点上的新订阅者在 SUBACK 之后都能收到保留值。
删除保留消息（空负载）同样会复制。

## 投递语义

- **每条路径至多一次。** 集群转发是尽力而为 HTTP。对端宕机时发往它的消息会丢失；
  MQTT QoS 不跨节点。
- **重复抑制。** 有界的已见消息缓存拒绝同一集群消息的重复投递。
- **会话亲和与接管。** 会话通常位于接受连接的节点。客户端以 `CleanStart=false`
  重连到另一节点时，该节点向前属主索要会话，恢复订阅与离线队列，并返回
  `session present = 1`。旧连接以 0x8E 关闭，不发布遗嘱。等待 PUBREL 的入站 QoS 2
  随会话迁移，客户端可以在新节点发送 PUBREL，消息在那里路由。
- **共享订阅跨集群。** 节点在心跳中广告 `$share` 组；每条消息只分配给托管该组的一个
  节点，按消息 ID 与组名哈希选择，因此其他节点上的组成员不会收到重复副本。
- **过滤器传播是最终一致的。** 新过滤器在下次心跳广告。订阅后立即发布，可能要到下次
  心跳（默认 5 秒）才到达远程订阅者。

## 安全

内部 API 对 `timestamp.body` 使用 HMAC-SHA256，允许五分钟时钟偏差。时间戳与签名放在
`X-NextMQTT-Timestamp` 与 `X-NextMQTT-Signature` 中。

- 密钥从不通过网络发送。
- 节点流量经过不可信网络时，`AdvertiseUrl` 应使用 HTTPS。
- 在 ingress 或 NetworkPolicy 中限制 `/internal/cluster/*`。
- 轮换密钥需要以新值重启节点；单一密钥暂不支持滚动轮换。

控制台登录与集群密钥相互独立。除非设置 `NEXTMQTT_ADMIN_TOKEN_SECRET`，每个节点用
自己的密钥签发 Bearer 令牌，因此一个节点签发的令牌会被另一节点拒绝。希望一次登录即可
使用任意节点控制台或 API 时，请在每个节点设置相同的值。

## Kubernetes 细节

StatefulSet 使用 headless 服务与 Pod DNS：

```text
NEXTMQTT_CLUSTER__NODEID=$(POD_NAME)
NEXTMQTT_CLUSTER__ADVERTISEURL=http://$(POD_NAME).nextmqtt-headless.nextmqtt.svc.cluster.local:8080
```

种子引用前三个 Pod 名称。扩容超过初始副本数后，新 Pod 会连接种子并通过 gossip 被发现。

## 验证集群

```bash
# 成员
curl -s http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN" | jq '.nodes'

# 跨节点投递
dotnet run --project src/NextMqtt.Cli -- sub --port 1884 --topic cluster/# --count 1 &
dotnet run --project src/NextMqtt.Cli -- pub --port 1883 --topic cluster/test --message hello --qos 1
```

订阅后至少等待一次心跳再发布。

## 排错

| 现象 | 检查 |
| --- | --- |
| 列表中只有一个节点 | 对端能否访问 `AdvertiseUrl`；种子拼写；8080 防火墙 |
| 对端日志出现 `401` | 密钥不一致，或时钟偏差超过 5 分钟 |
| 节点在 alive/dead 间反复 | 心跳间隔与超时；CPU 饥饿 |
| 对端缺少保留消息 | 对端在同步机制之前加入；重启或重新发布 |
| 远程订阅者漏消息 | 过滤器尚未传播；等待一次心跳 |
| 远程投递重复 | 消息 ID 缓存过早清理；检查节点时钟与负载 |
| 重启后限额看起来偏低 | 节点仍把死节点计入，直到 3 倍 `NodeTimeoutSeconds` |

## 限制

- 会话接管覆盖订阅、离线队列，以及仍在等待 PUBREL 的入站 QoS 2 发布；出站 inflight
  确认状态不迁移，消息会从队列重放，客户端可能看到一次重复。
- 副本最多滞后 `SessionReplicaSeconds`，且只存在对端内存里；所有其他节点同时重启时
  副本也会丢。设置 `Storage:Replicas:Mode=File` 可跨重启保留。
- 除接管与复制外没有跨节点会话持久化：如果属主、所有副本持有者及其磁盘一起丢失，
  会话就没了。
- 过滤器广告是全量快照，默认每次心跳最多 10,000 个过滤器。
- 集群路由按链路至少一次并使用去重缓存，不是事务保证。
- 保留消息同步只在加入时进行一次；与网络分区的节点会继续提供自己的旧保留值，直到
  重新连接。
