# 协议支持

NextMQTT 实现 OASIS MQTT 3.1.1（level 4）与 MQTT 5.0（level 5）。

## 传输

| 传输 | 默认端口 | 说明 |
| --- | --- | --- |
| TCP | 1883 | 明文 MQTT |
| TLS | 8883 | 可选证书 |
| WebSocket | 8083 `/mqtt` | 子协议 `mqtt` |
| WSS | 8084 `/mqtt` | 需 HTTPS 证书 |

## 报文

| 报文 | 3.1.1 | 5.0 | 说明 |
| --- | --- | --- | --- |
| CONNECT / CONNACK | ✅ | ✅ | 会话、遗嘱、属性 |
| PUBLISH | ✅ | ✅ | QoS 0/1/2、retain、入站别名 |
| PUBACK/PUBREC/PUBREL/PUBCOMP | ✅ | ✅ | 完整 QoS 2 |
| SUBSCRIBE / SUBACK | ✅ | ✅ | 通配符、共享订阅、订阅标识 |
| UNSUBSCRIBE / UNSUBACK | ✅ | ✅ | 5.0 逐过滤器原因码 |
| PINGREQ / PINGRESP | ✅ | ✅ | 保活 |
| DISCONNECT | ✅ | ✅ | 原因码、会话过期更新 |
| AUTH | — | ✅ | 可插拔提供方的挑战/应答，内置 `nextmqtt-jwt` |

## 已实现

- 持久会话、会话过期、会话接管、离线队列；跨节点重连时向属主或最新副本索要会话，未完成的入站 QoS 2 随会话迁移
- 保留消息、消息过期、遗嘱与遗嘱延迟
- `+` / `#` 通配符与 `$` 首层规则
- 共享订阅 `$share/{group}/{filter}`，每条消息在集群范围内选定一个组成员
- `noLocal`、`retainAsPublished`、retain handling
- MQTT 5 用户属性、内容类型、响应主题、关联数据透传
- 入站与出站主题别名（受 `MaxTopicAlias` 与客户端 Topic Alias Maximum 限制）
- 最大报文、inflight、队列、订阅配额
- 保活 1.5 倍超时断开

## 限制

| 限制 | 默认 |
| --- | --- |
| 最大报文 | 1 MiB |
| 每会话 inflight | 32 |
| 离线队列 | 1000 |
| 每会话订阅 | 1000 |
| 主题别名 | 32 |
| CONNECT 超时 | 10 秒 |
| 重传间隔 | 10 秒 |

MQTT 规范上限仍然生效：remaining length 268,435,455 字节、字段 65,535 字节、
包 ID 65,535。

## 错误处理

| 原因码 | 场景 |
| --- | --- |
| `0x81` | 报文畸形、标志位/UTF-8 错误 |
| `0x82` | 状态机/包 ID 非法 |
| `0x8F` | SUBSCRIBE 过滤器非法 |
| `0x90` | PUBLISH 主题非法 |
| `0x94` | 主题别名非法 |
| `0x95` | 报文超限 |
| `0x87` | ACL 拒绝 |
| `0x8D` | 保活超时 |

## 当前非目标

- MQTT over QUIC
- 跨节点顺序保证与 exactly-once；集群转发为尽力而为
- 与第三方 Broker 插件或扩展包的运行时兼容
- 非对称 JWT 签名；只接受 HS256

详见[功能对齐清单](/zh/features)与[路线图](/zh/roadmap)。
