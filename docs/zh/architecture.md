# 架构

NextMQTT 是模块化单体：一个进程、少量职责明确的组件，以及数据面与控制面之间的清晰
边界。本页说明设计、数据结构、投递路径与取舍。

## 设计原则

1. **数据面隔离。** MQTT 连接、会话与路由独立于 HTTP API 和控制台运行。慢的管理请求
   不会阻塞发布。
2. **一切有界。** 每会话 inflight 窗口、离线队列、报文大小上限、每连接有界出站
   Channel。背压通过丢弃指标体现，而不是内存悄悄增长。
3. **会话单一属主。** 一个客户端 ID 同时只属于一个节点；节点内的会话接管是显式且无
   竞争的。
4. **控制面可替换。** REST API 与控制台使用与协议引擎相同的 Broker 门面，没有私有
   后门。
5. **清室独立。** 行为遵循 OASIS MQTT 规范，不依赖其他 MQTT 产品的内部实现。

## 组件图

```text
                         ┌─────────────────────────────────────────────┐
   MQTT/TCP, MQTT/TLS    │                 NextMqtt.Server             │
   ────────────────────► │  Kestrel (API, 控制台, WebSocket /mqtt)      │
                         │  TCP/TLS 监听器 (BackgroundService)          │
                         └──────────────────────┬──────────────────────┘
                                                │ 每个 socket 一个 ClientConnection
                         ┌──────────────────────▼──────────────────────┐
                         │                 NextMqtt.Broker             │
                         │                                             │
                         │  ClientConnection ──► BrokerSession         │
                         │        │                    │               │
                         │        ▼                    ▼               │
                         │  MqttBroker.RouteAsync ──► SubscriptionIndex│
                         │        │                    │               │
                         │        ├──► RetainedMessageStore            │
                         │        └──► ClusterRouter ──► 对端          │
                         │                                             │
                         │  ClientAuthenticator / TopicAuthorizer      │
                         │  BrokerMetrics / RateTracker                │
                         └──────────────────────┬──────────────────────┘
                                                │
                         ┌──────────────────────▼──────────────────────┐
                         │                  NextMqtt.Core              │
                         │  MQTT 编解码、主题过滤器、主题 Trie         │
                         └─────────────────────────────────────────────┘
```

### `NextMqtt.Core`

- MQTT 3.1.1/5.0 报文模型、解码器、编码器与 `PipeReader` 组帧。
- 严格的 UTF-8 与属性校验。
- 主题过滤器解析/匹配，以及订阅与保留索引共用的通用 Trie。
- 无网络、无日志、无第三方依赖。

### `NextMqtt.Broker`

- `ClientConnection`：一个 socket、协议状态机、保活、主题别名、出站 Channel 与
  优雅关闭。
- `BrokerSession`：持久会话状态、inflight 窗口、离线队列、入站 QoS 2 去重。
- `MqttBroker`：认证、会话绑定/接管、路由、保留消息、管理与维护。
- `SubscriptionIndex`、`RetainedMessageStore`、`TopicAuthorizer`、
  `ClientAuthenticator`、`ClusterRouter`、指标。

### `NextMqtt.Server`

- 托管 Kestrel，提供 REST、控制台静态文件与 MQTT over WebSocket。
- 运行原始 TCP 与 TLS 监听器。
- 映射签名的集群内部 API。
- 初始化管理员凭据并分发 Vue 前端包。

### `NextMqtt.Cli`

- 使用共享编解码的 `pub`、`sub`、`bench`、`hash-password`。
- 适合冒烟测试、集群检查与负载实验。

## 连接生命周期

1. 监听器接受 socket，创建 `ClientConnection`。
2. 首包必须是 CONNECT，超时由 `Broker:ConnectTimeout` 控制。
3. 校验或生成客户端 ID；允许时由 Broker 为零长度 ID 分配。
4. `ClientAuthenticator` 依次尝试本地密码、密码字段中的 JWT、启用时的匿名访问。
5. `MqttBroker.AttachSessionAsync` 串行化会话绑定：
   - clean start 或协议版本变化时丢弃旧会话；
   - 否则恢复会话（`session present = 1`）；
   - 会话在别的节点上时，先向属主索要；属主不可达或答复没有该会话时回退到最新副本；
   - 旧连接收到 DISCONNECT `0x8E`（会话被接管）。
6. 直接写出 CONNACK，随后启动写循环并重发 inflight、清空离线队列。
7. 读循环处理报文，直到 DISCONNECT、保活超时、协议错误或 socket 断开。
8. 清理连接、安排遗嘱、按过期时间保留或删除会话，并在会话结束时释放订阅。

## 数据结构

### 主题 Trie

`TopicTrie<TValue>` 是以主题层级为键的前缀树。

- 订阅匹配沿主题下行，收集精确节点、`+` 子节点与 `#` 子节点的值。
- 保留消息检索沿过滤器下行：`#` 收集子树，`+` 遍历子节点，字面量继续下降。
- `$` 主题规则在根节点生效：通配符永远不匹配以 `$SYS` 这类层级开头的主题。
- 删除会剪掉空分支；节点上的桶可以保存多个值。

### 订阅索引

- 普通订阅位于一个以实际过滤器为键的 Trie。
- 共享订阅按 `(group, filter)` 分组；每组是一个轮询成员列表。
- 每客户端字典提供 O(1) 查找与断线清理。
- 发布会产生 `(subscription, sharedGroup)` 匹配列表，Broker 再按客户端聚合，避免
  重叠过滤器造成重复投递。

### 会话

`BrokerSession` 持有：

| 字段 | 用途 |
| --- | --- |
| `_inflight` | 等待 PUBACK/PUBREC/PUBCOMP 的出站 QoS 1/2 报文 |
| `_queue` | 持久会话的有界离线队列 |
| `_inboundQos2` | 等待 PUBREL 的包 ID，防止重复投递 |
| `_nextPacketId` | 单调递增的包 ID 分配器，跳过 inflight 中的 ID |
| `ExpiresAt` | 绝对会话过期时间，由维护循环评估 |

所有会话变更持有会话锁。投递到出站 Channel 是非阻塞的；Channel 满时记为丢弃，而不
阻塞路由线程。

### 保留消息

保留消息以精确主题存入 Trie，检索使用与订阅一致的通配符语义。空负载的 retain 发布
表示删除。消息过期时间转换为绝对截止时间，在读取与维护时校验。

## 发布路径

```text
PUBLISH ──► 校验主题/别名 ──► ACL 发布检查
        ──► 更新保留消息（retain）
        ──► 订阅 Trie 匹配
        ──► 按客户端聚合（最大 QoS、RAP、订阅标识）
        ──► 会话队列/inflight ──► 连接出站 Channel
        ──► 集群转发（普通订阅 + 保留消息复制）
```

投递 QoS 为 `min(发布 QoS, 订阅 QoS)`。实时转发的 retain 标志默认清零，除非订阅设置
了 `retainAsPublished`。SUBSCRIBE 的订阅标识会附加到投递给 MQTT 5 客户端的消息。

## QoS 状态机

**出站 QoS 1：** PUBLISH → PUBACK。未确认的报文按 `Broker:RetransmitInterval` 以
DUP=1 重传。

**出站 QoS 2：** PUBLISH → PUBREC → PUBREL → PUBCOMP。PUBREL 持续重传直到 PUBCOMP。

**入站 QoS 2：** 先按包 ID 暂存并回 PUBREC，收到 PUBREL 后才路由，然后回 PUBCOMP；
重复包 ID 不会产生第二次投递。

## 集群

节点通过签名 HTTP 组成网状结构：

- 每个节点在心跳中广播自己的**普通**订阅过滤器。
- 成员关系通过 gossip 传播：响应包含对端的成员表，因此只需一个可达种子。
- 发布只转发给广告了匹配过滤器的存活节点；retain 发布复制到所有存活节点，使保留
  消息最终收敛。
- 会话快照每 `SessionReplicaSeconds` 推送给对端；新节点向属主索要会话，只有属主不可
  用、不可达或答复没有该会话时才使用最新副本。
- 消息 ID 用于去重；来自远端的发布不再转发，避免环路。
- 共享订阅在集群范围内协调：每条消息为共享组选定一个节点，不会每个节点各投一次。
  详见[集群](/zh/clustering)。

内部 API 使用 HMAC-SHA256（时间戳 + 原始 body）签名，允许五分钟时钟偏差。启用集群时
共享密钥为必填项。

## 并发与背压

- 每连接一个读循环、一个写循环。
- `System.IO.Pipelines` 组帧，避免逐包拷贝。
- 1024 帧的有界 Channel 解耦路由与 socket 写入。
- 会话 inflight 窗口限制未确认的 QoS 流量。
- 维护循环清理过期保留消息与会话，并触发重传。
- 丢弃与重传均有指标，过载可观测。

## 持久化与故障模型

| 事件 | 行为 |
| --- | --- |
| 客户端断开 | 会话过期时间非零或 clean start=false 时保留会话 |
| Broker 重启 | 启用文件存储时从磁盘恢复保留消息与持久会话；否则丢失 |
| 集群节点崩溃 | 其他节点先标记 suspect，再标记 dead，3 倍超时后移除；会话由属主文件或最新副本恢复 |
| 全集群重启 | 各节点重载会话与副本文件，按 `(lastSeen, nodeId)` 选最新副本 |
| 网络分区 | 各自继续服务，转发到不可达节点失败并静默记录 |
| 慢消费者 | 出站 Channel 满后丢弃并计数 |
| 协议违规 | 使用合适的 MQTT 5 reason code 关闭连接 |

持久化按数据集合分别开启：`Storage:Retained:Mode=File` 记录保留消息日志，
`Storage:Session:Mode=File` 为持久会话做快照，`Storage:Replicas:Mode=File` 持久化
会话副本。出站 inflight 确认状态不落盘，消息本身会随副本和接管作为队列消息重放（可能重复）；等待 PUBREL 的
入站 QoS 2 发布属于快照的一部分，因此握手可以完成。保留消息是最后已知值存储，
不是持久化消息日志。

## 扩展性

- 优先纵向扩容；热路径注重减少分配，锁粒度按会话和索引划分。
- 节点拥有自己的会话，但客户端可重连到任意节点：会话会从属主迁移，或从副本恢复。
- 集群路由成本随节点数与过滤器数量增长，默认每次心跳最多 10,000 个过滤器。
- 共享订阅组跨节点协调，因此每条消息每个组只投递一次，而不是每个节点一次。

## 明确的非目标

- 不提供第三方 Broker 运行时兼容层或私有插件 API。
- 不要求外部数据库；持久化是每节点文件方式。
- 不声称与任何其他 Broker 兼容认证或获得背书。

这些能力将来可能作为独立模块加入，见[路线图](/zh/roadmap)。
