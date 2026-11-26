# 协议支持

NextMQTT 实现 OASIS 规范中的 MQTT 3.1.1（协议级别 4）与 MQTT 5.0（级别 5）。

## 传输

| 传输 | 默认端口 | 说明 |
| --- | --- | --- |
| TCP | 1883 | 明文 MQTT |
| TLS | 8883 | 可选，需要证书 |
| WebSocket | 8083（`/mqtt`） | 请求时协商子协议 `mqtt` |
| 安全 WebSocket | 8084（`/mqtt`） | 可选 Kestrel HTTPS 端点 |

WebSocket 客户端连接 `ws://host:8083/mqtt` 或 `wss://host:8084/mqtt`。同一个 HTTP
端点同时提供控制台与 REST API。

## 控制报文

| 报文 | 3.1.1 | 5.0 | 说明 |
| --- | --- | --- | --- |
| CONNECT | ✅ | ✅ | 用户名/密码、遗嘱、clean start、会话过期 |
| CONNACK | ✅ | ✅ | Session present、原因码、服务器属性 |
| PUBLISH | ✅ | ✅ | QoS 0/1/2、retain、DUP、入站与出站主题别名 |
| PUBACK | ✅ | ✅ | 5.0 含原因码与属性 |
| PUBREC / PUBREL / PUBCOMP | ✅ | ✅ | 完整 QoS 2 握手 |
| SUBSCRIBE | ✅ | ✅ | 通配符、共享订阅、订阅标识 |
| SUBACK | ✅ | ✅ | 逐过滤器原因码 |
| UNSUBSCRIBE / UNSUBACK | ✅ | ✅ | 5.0 逐过滤器原因码 |
| PINGREQ / PINGRESP | ✅ | ✅ | 保活 |
| DISCONNECT | ✅ | ✅ | 原因码、会话过期更新、断开时遗嘱 |
| AUTH | — | ✅ | 可插拔提供方的挑战/应答，内置 `nextmqtt-jwt` |

## 功能

### 会话

- Clean start 与持久会话。
- MQTT 5 会话过期间隔，包括在 DISCONNECT 中更新。
- 会话接管：旧连接收到 DISCONNECT `0x8E`。
- 客户端以 `CleanStart=false` 在另一节点重连时，向属主索要会话或从副本恢复；等待
  PUBREL 的入站 QoS 2 随会话迁移。
- 持久会话的离线队列，上限由 `Broker:MaxQueuedMessages` 控制。
- 零长度客户端 ID 由 Broker 分配（通过 `AssignedClientIdentifier` 返回）。

### 订阅

- `+` 与 `#` 通配符，以及首层 `$` 排除规则。
- 共享订阅：`$share/{group}/{filter}`；每条消息分配给集群范围内该组的一个成员。
- MQTT 5 选项：`noLocal`、`retainAsPublished`、retain handling 0/1/2。
- 匹配投递会附带订阅标识。
- 重叠订阅按客户端聚合，以最高匹配 QoS 只投递一次。

### 消息

- 保留消息，包括用空负载删除。
- 保留与实时消息的消息过期间隔。
- 带遗嘱延迟的遗嘱；正常 DISCONNECT 会抑制（也可用原因码 `0x04` 显式发布）。
- MQTT 5 用户属性、内容类型、响应主题与关联数据端到端保留。
- 入站与出站主题别名，受 `Broker:MaxTopicAlias` 与客户端的 Topic Alias Maximum
  限制。
- 每个监听器通过 `Broker:MaxPacketSize` 限制最大报文。
- 在 CONNACK 中通告 Receive Maximum，并按会话限制未确认的入站 QoS 2 数量。

### 保活

客户端必须在保活间隔的 1.5 倍内发送任意控制报文。超时后 Broker 以原因码 `0x8D`
（MQTT 5）关闭连接。保活为 `0` 时关闭检查。

## 限制

| 限制 | 默认 | 选项 |
| --- | --- | --- |
| 报文大小 | 1 MiB | `Broker:MaxPacketSize` |
| 每会话 inflight | 32 | `Broker:MaxInflightMessages` |
| 离线队列 | 1,000 条 | `Broker:MaxQueuedMessages` |
| 每会话订阅 | 1,000 | `Broker:MaxSubscriptionsPerSession` |
| 主题别名 | 32 | `Broker:MaxTopicAlias` |
| 客户端 ID 长度 | 256 | `Broker:MaxClientIdLength` |
| CONNECT 超时 | 10 秒 | `Broker:ConnectTimeout` |
| 重传间隔 | 10 秒 | `Broker:RetransmitInterval` |

MQTT 自身的上限仍然生效：剩余长度 268,435,455 字节、每个 UTF-8 字段 65,535 字节、
同时 65,535 个包 ID。

## 错误处理

畸形报文会关闭连接。使用 MQTT 5 时，Broker 会尝试以具体原因码发送 DISCONNECT：

| 原因码 | 场景 |
| --- | --- |
| `0x81` Malformed packet | 标志位非法、字段截断、UTF-8 错误 |
| `0x82` Protocol error | 状态转换非法、包 ID 为 0 |
| `0x8F` Topic filter invalid | SUBSCRIBE 中的过滤器非法 |
| `0x90` Topic name invalid | PUBLISH 主题为空或含通配符 |
| `0x94` Topic alias invalid | 别名超出协商范围 |
| `0x95` Packet too large | 报文超过配置上限 |
| `0x87` Not authorized | ACL 拒绝发布或订阅 |
| `0x8D` Keep-alive timeout | 保活 1.5 倍内没有报文 |

MQTT 3.1.1 客户端在连接失败时收到最接近的 CONNACK 返回码，协议错误时直接断开。

## 当前非目标

- MQTT over QUIC。
- 跨节点顺序保证与 exactly-once；集群转发为尽力而为。
- 与第三方 Broker 插件或扩展包的运行时兼容。
- 非对称 JWT 签名；只接受 HS256。

详见[路线图](/zh/roadmap)。
