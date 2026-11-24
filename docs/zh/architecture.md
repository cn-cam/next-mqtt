# 架构设计

NextMQTT 采用模块化单体：一个进程、少量清晰定义的组件，并明确区分数据面与
控制面。

## 设计原则

1. **数据面隔离**：MQTT 连接、会话与路由独立于 HTTP API 和控制台；慢请求不会
   阻塞消息发布。
2. **处处有界**：会话 inflight 窗口、离线队列、报文大小、每连接有界 Channel。
   背压通过丢弃指标显式呈现，而不是悄悄耗尽内存。
3. **会话单一归属**：一个客户端 ID 在任一时刻只属于一个节点；会话接管有明确语义。
4. **控制面可替换**：REST API 与控制台使用与协议引擎相同的 Broker 门面。
5. **净室独立实现**：行为遵循 OASIS MQTT 规范，不复制其他 Broker 的实现。

## 组件

```text
                    ┌───────────────────────────────────────────┐
MQTT/TCP、TLS ────► │              NextMqtt.Server              │
                    │  Kestrel（API、控制台、WebSocket /mqtt）  │
                    │  TCP/TLS 监听器（BackgroundService）      │
                    └─────────────────────┬─────────────────────┘
                                          │ 每 socket 一个 ClientConnection
                    ┌─────────────────────▼─────────────────────┐
                    │              NextMqtt.Broker              │
                    │  ClientConnection ──► BrokerSession       │
                    │  MqttBroker.RouteAsync ──► SubscriptionIndex
                    │  RetainedMessageStore / ClusterRouter     │
                    │  ClientAuthenticator / TopicAuthorizer    │
                    │  BrokerMetrics / RateTracker              │
                    └─────────────────────┬─────────────────────┘
                    ┌─────────────────────▼─────────────────────┐
                    │               NextMqtt.Core                │
                    │  MQTT 编解码、主题过滤器、主题 Trie         │
                    └───────────────────────────────────────────┘
```

### 各项目职责

- **NextMqtt.Core**：MQTT 3.1.1/5.0 报文模型、解码/编码、`PipeReader` 组帧、
  严格 UTF-8 与属性校验、主题过滤器与通用 Trie。无网络、无日志、无第三方依赖。
- **NextMqtt.Broker**：连接状态机、会话、路由、保留消息、认证、ACL、集群与指标。
- **NextMqtt.Server**：Kestrel、原始 TCP/TLS 监听、集群内部 API、静态控制台资源、
  管理凭据初始化。
- **NextMqtt.Cli**：`pub` / `sub` / `bench` / `hash-password`，复用同一套编解码。

## 连接生命周期

1. 监听器接受 socket，创建 `ClientConnection`。
2. 首包必须是 CONNECT，超时由 `Broker:ConnectTimeout` 控制。
3. 校验或生成客户端 ID（允许零长度时由 Broker 分配）。
4. 认证顺序：本地密码 → JWT（放在密码字段）→ 匿名（如启用）。
5. `MqttBroker.AttachSessionAsync` 串行化会话绑定：
   - clean start 或协议版本变化时丢弃旧会话；
   - 否则恢复会话（`session present=1`）；
   - 旧连接收到 DISCONNECT `0x8E`（会话被接管）。
6. 直接写出 CONNACK，随后启动写循环并重发 inflight、清空离线队列。
7. 读循环处理报文，直到 DISCONNECT、保活超时、协议错误或 socket 断开。
8. 清理连接、安排遗嘱、按过期时间保留或删除会话，并在会话结束时释放订阅。

## 核心数据结构

### 主题 Trie

`TopicTrie<TValue>` 以主题层级为键：

- 订阅匹配沿主题下行，收集当前节点、`+` 子节点与 `#` 子节点的值；
- 保留消息检索沿过滤器下行，`#` 收集子树、`+` 遍历子节点；
- 根节点强制 `$` 主题规则：通配符不匹配以 `$SYS` 类开头的层级；
- 删除时裁剪空分支，同一节点可挂多个值。

### 订阅索引

- 普通订阅存放在一个 Trie 中（键为有效过滤器）。
- 共享订阅按 `(组, 过滤器)` 分组，组内轮询选择成员。
- 每客户端字典用于 O(1) 查找与断线清理。
- 发布时得到 `(subscription, sharedGroup)` 列表，再按客户端聚合成一条消息，避免
  重叠订阅造成重复投递。

### 会话

`BrokerSession` 持有：

| 字段 | 用途 |
| --- | --- |
| `_inflight` | 等待 PUBACK/PUBREC/PUBCOMP 的出站 QoS 1/2 报文 |
| `_queue` | 持久会话的有界离线队列 |
| `_inboundQos2` | 等待 PUBREL 的入站包 ID，用于去重 |
| `_nextPacketId` | 递增包 ID 分配器，跳过 inflight 中已用 ID |
| `ExpiresAt` | 绝对会话过期时间，由维护任务扫描 |

所有会话变更持有会话锁。投递到出站 Channel 是非阻塞的；Channel 满时记录丢弃，
而不是阻塞路由线程。

### 保留消息

保留消息以精确主题存入 Trie，检索使用与订阅一致的通配符语义。空负载的 retain
发布表示删除。消息过期时间转换为绝对截止时间，读取与维护时都会校验。

## 发布路径

```text
PUBLISH ──► 校验主题/别名 ──► ACL 发布检查
        ──► 更新保留消息（retain）
        ──► 订阅 Trie 匹配
        ──► 按客户端聚合（最大 QoS、RAP、订阅标识）
        ──► 会话队列/inflight ──► 连接出站 Channel
        ──► 集群转发（普通订阅 + 保留消息复制）
```

投递 QoS 为 `min(发布 QoS, 订阅 QoS)`。实时转发的 retain 标志默认清零，除非订阅
设置了 `retainAsPublished`。MQTT 5 订阅标识会附加到投递消息。

## QoS 状态机

- **出站 QoS 1**：PUBLISH → PUBACK，未确认则按 `RetransmitInterval` 以 DUP=1 重传。
- **出站 QoS 2**：PUBLISH → PUBREC → PUBREL → PUBCOMP，PUBREL 持续重传直到 PUBCOMP。
- **入站 QoS 2**：先按包 ID 暂存并回 PUBREC，收到 PUBREL 后才路由，然后回 PUBCOMP；
  重复包 ID 不会产生第二次投递。

## 集群

节点通过签名 HTTP 组成网状结构：

- 每个节点在心跳中广播自己的**普通**订阅过滤器。
- 成员关系通过 gossip 传播：响应包含接收方已知成员表，因此只需一个可达种子。
- 发布只转发给广告了匹配过滤器的存活节点；retain 发布复制到所有存活节点，使保留
  消息最终收敛。
- 消息 ID 用于去重；来自远端的发布不再转发，避免环路。
- 共享订阅按节点独立计算，而非全局协调。这是为保持传输无状态而做的取舍，详见
  [集群](/clustering)。

内部 API 使用 HMAC-SHA256（时间戳 + 原始 body）签名，允许五分钟时钟偏差。启用
集群时共享密钥为必填项。

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
| Broker 重启 | 会话、队列、保留消息丢失（当前为内存存储） |
| 集群节点崩溃 | 其他节点先标记 suspect，再标记 dead，3 倍超时后移除 |
| 网络分区 | 各自继续服务，转发到不可达节点失败并静默记录 |
| 慢消费者 | 出站 Channel 满后丢弃并计数 |
| 协议违规 | 使用合适的 MQTT 5 reason code 关闭连接 |

持久化后端在[路线图](/roadmap)中。在此之前，请把 Broker 视为有状态服务，并将
保留消息用于可恢复的最后已知值，而不是持久化消息日志。

## 扩展性

- 优先纵向扩容；热路径注重减少分配，锁粒度按会话和索引划分。
- 节点拥有自己的会话；客户端可重连到任意节点，但只会在会话所属节点恢复。
- 集群路由成本随节点数与过滤器数量增长，默认每次心跳最多 10,000 个过滤器。
- 共享订阅组不跨节点协调；除非需要全局语义，否则按节点 worker 池使用。

## 明确的非目标

- 不提供 Erlang/BEAM 兼容层，不复制第三方 Broker 插件 API。
- 不内置规则引擎、桥接或外部数据库依赖。
- 不声称与任何其他 Broker 兼容认证或获得背书。
