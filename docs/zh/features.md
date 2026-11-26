# 功能对齐清单

本页是当前版本的实现契约：明确区分“已实现”“部分实现”和“未实现”，避免部署决策
依赖宣传文案。

图例：✅ 已实现 · ⚠️ 部分实现或有限制 · ❌ 未实现

## 协议与传输

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| MQTT 3.1.1（level 4） | ✅ | 编解码、标志位、CONNACK 返回码 |
| MQTT 5.0（level 5） | ✅ | 核心报文与属性 |
| TCP | ✅ | 默认 1883 |
| TLS | ✅ | 可选 PFX/PEM 证书，TLS 1.2/1.3 |
| WebSocket | ✅ | `/mqtt`，子协议 `mqtt` |
| 安全 WebSocket | ✅ | Kestrel HTTPS + `/mqtt` |
| QoS 0/1/2 | ✅ | 双向、重传、去重 |
| 保留消息 | ✅ | 通配符检索、过期、删除 |
| 遗嘱与遗嘱延迟 | ✅ | 正常 DISCONNECT 时抑制 |
| 会话过期 | ✅ | MQTT 5 interval，可在 DISCONNECT 更新 |
| 会话接管 | ✅ | 旧连接收到 `0x8E` |
| 离线队列 | ✅ | 每个持久会话有界 |
| 共享订阅 | ✅ | `$share/{group}/{filter}`，节点内轮询 |
| 订阅选项 | ✅ | `noLocal`、`retainAsPublished`、retain handling |
| 主题别名 | ⚠️ | 支持入站；出站别名计划中 |
| 增强认证（`AUTH`） | ✅ | 可插拔提供方的挑战/应答；内置 `nextmqtt-jwt` |
| 服务端 receive-maximum 强制 | ⚠️ | 广播并限制自身 inflight 窗口 |
| 客户端最大报文限制 | ⚠️ | 入站已限制；出站未按客户端上限裁剪 |
| MQTT over QUIC | ❌ | 研究项 |
| 出站 webhook 桥接 | ✅ | 每规则有界队列、重试、仅源节点推送 |
| 插件宿主 | ✅ | 清单 + 可回收加载上下文；可选择认证器/授权器 |
| 规则引擎动作 | ✅ | 存储前按主题过滤 `drop` 与前缀 `rewrite` |
| NATS 桥接 | ✅ | 文本协议发布、有界队列、自动重连 |
| Kafka 桥接 | ✅ | Confluent.Kafka 生产者，过滤与有界队列同 NATS |
| RabbitMQ 桥接 | ✅ | 持久 topic 交换机、publisher confirm、自动重连 |

## 会话与投递

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| Clean Start / Session Present | ✅ | 3.1.1 与 5.0 语义 |
| 持久会话 | ✅ | 默认内存，可选落盘快照 |
| 会话过期清理 | ✅ | 后台维护循环 |
| QoS 1/2 inflight 窗口 | ✅ | 每会话可配置 |
| 包 ID 分配 | ✅ | 跳过 inflight 中已用 ID |
| 离线队列上限 | ✅ | 丢弃最旧消息并计数 |
| 重叠订阅聚合 | ✅ | 每客户端一条，取最高 QoS |
| 重启后状态保留 | ✅ | 保留日志 + 会话/副本快照；出站 inflight 仍由客户端重传 |
| 会话备份与恢复 | ✅ | `GET`/`POST /api/v1/backup` 导入导出保留消息与会话 |
| 优雅停机 | ✅ | 关闭前等待 inflight 确认，上限 `Server:DrainTimeoutSeconds` |
| 跨节点会话迁移 | ✅ | `CleanStart=false` 时向属主或最新副本索要会话 |

## 安全

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| PBKDF2-SHA256 密码 | ✅ | 版本化格式，210k 迭代 |
| HS256 JWT | ✅ | exp/nbf/issuer/audience/roles |
| 匿名访问 | ✅ | 必须显式开启 |
| 主题 ACL | ✅ | 有序规则、身份占位符、glob |
| 保留消息读取 ACL | ✅ | 投递时再次校验 |
| TLS | ✅ | MQTT、控制台/WSS |
| 控制台令牌认证 | ✅ | HS256 Bearer |
| API Key | ✅ | SHA-256 摘要白名单 |
| 控制台 RBAC | ❌ | 当前仅管理员角色 |
| 连接/消息速率限制 | ✅ | 按来源地址与按客户端的令牌桶 |
| 主题级发布配额 | ✅ | 按主题过滤器共享令牌桶 |
| 出站主题别名 | ✅ | 在客户端声明的 Topic Alias Maximum 内按连接分配别名 |
| 订阅标识配额 | ✅ | `MaxSubscriptionIdentifiersPerClient` 限制每客户端带标识的订阅数 |
| mTLS 策略 | ⚠️ | 可选客户端证书，平台默认校验 |

## 集群

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| Gossip 成员管理 | ✅ | 心跳、种子发现、suspect/dead |
| 签名节点通信 | ✅ | HMAC-SHA256，5 分钟时间窗 |
| 跨节点路由 | ✅ | 仅转发给广告了匹配过滤器的节点 |
| 保留消息复制 | ✅ | 尽力复制到存活节点 |
| 消息去重 | ✅ | 有界已见消息缓存 |
| 环路防止 | ✅ | 远端消息不再转发 |
| 全局共享订阅 | ✅ | 每条消息为每个共享组选定唯一节点 |
| 重连时会话接管 | ✅ | `CleanStart=false` 时向前一节点索要会话，含离线队列与未完成的入站 QoS 2 |
| 会话副本落盘 | ✅ | 可选文件存储，整集群重启后副本仍然可用 |
| 跨节点 QoS 保证 | ❌ | 转发为尽力而为 HTTP |
| 无重启轮换集群密钥 | ❌ | 单一共享密钥 |
| 大规模过滤器路由 | ⚠️ | 每心跳全量快照，上限 10,000 |

## 运维与管理

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| REST 管理 API | ✅ | 客户端、订阅、保留、发布、集群、日志、插件、配置 |
| Vue 3 控制台 | ✅ | 中英文、深浅主题、实时轮询、插件与日志页面 |
| 插件热重载 | ✅ | `POST /api/v1/plugins/reload` 重新读取配置、扫描目录并替换认证与授权 |
| 插件生命周期 | ✅ | 实现 `IPluginLifecycle` 可在卸载前收到 `Initialize` 与 `Dispose` |
| MQTT 调试器 | ✅ | 浏览器经 WebSocket 直连：MQTT 3.1.1 / 5.0、QoS 0–2、MQTT 5 属性、订阅、报文跟踪、日志导出 |
| Prometheus 指标 | ✅ | `/metrics`，计数器与 gauge |
| JSON 统计 | ✅ | 60 秒发布序列 |
| 健康检查 | ✅ | `/healthz`、`/readyz` |
| 结构化日志 | ✅ | stdout，可配置级别 |
| CLI | ✅ | `pub`、`sub`、`bench`、`hash-password` |
| Docker 镜像 | ✅ | 多架构、非 root、健康检查 |
| Compose 单机/三节点 | ✅ | `docker-compose.yml`、`deploy/compose` |
| Kustomize | ✅ | StatefulSet、PDB、HPA、Ingress、NetworkPolicy |
| Helm | ✅ | 探针、自动扩缩容、Ingress、existingSecret |
| 持久化/备份 | ✅ | 保留消息日志、会话快照与备份 API |
| 分布式追踪 | ❌ | 路线图 |

## 验证方式

| 检查 | 位置 |
| --- | --- |
| 报文往返与畸形输入 | `tests/NextMqtt.Core.Tests` |
| 主题匹配与保留消息 Trie | `tests/NextMqtt.Core.Tests` |
| 会话、QoS、队列、ACL、认证、集群路由 | `tests/NextMqtt.Broker.Tests` |
| 真实 TCP CONNECT/SUBSCRIBE/PUBLISH/QoS | `tests/NextMqtt.Server.Tests` |
| 控制台类型检查与生产构建 | CI `Dashboard` |
| 文档构建 | CI `Documentation` |
| Helm/Kustomize 渲染 | CI `Kubernetes manifests and Helm chart` |
| 镜像构建与健康/登录冒烟 | CI `Container image` |
| CodeQL | `codeql.yml` |

当前共 **211 个测试通过**（63 核心、105 Broker、43 Server 集成）。

## 扩展性

NextMQTT 同时提供进程内扩展接口与插件宿主。插件由 `plugin.json` 清单加载到可回收
上下文，契约版本为 1；清单指向不存在的程序集或契约版本不符时会被跳过。

| 扩展点 | 接口 | 用途 |
| --- | --- | --- |
| 客户端认证 | `IClientAuthenticator` | 替换或扩展用户名/密码与 API Key 校验 |
| 主题授权 | `ITopicAuthorizer` | 按客户端放行或拒绝发布/订阅 |
| 集群路由 | `IClusterRouter` | 替换 HTTP 转发策略 |
| 插件生命周期 | `IPluginLifecycle` | 在加载上下文卸载前释放资源 |

接口通过依赖注入解析，因此也可以直接编译进程序。插件是免重新构建的磁盘方案：
`Plugins:Authenticator` 与 `Plugins:Authorizer` 指定要激活的类型，
`POST /api/v1/plugins/reload` 无需重启即可生效。进程外集成使用带版本的管理 API
（`/api/v1`）与集群端点（`/internal/cluster`）。

## 已知限制

- 出站 `inflight` 确认状态不跨节点迁移；这些消息从队列重放，可能重复到达。等待
  PUBREL 的入站 QoS 2 发布随会话一起迁移。
- 副本的新鲜度取决于 `Cluster:SessionReplicaSeconds`；未开启
  `Storage:Replicas:Mode=File` 时，所有节点重启会丢失副本。
- 集群转发为尽力而为；共享订阅在节点间协调。
- 跨节点不保证顺序，也不保证 exactly-once。
- 进程内扩展需要改源码并重新构建；插件宿主是运行时的替代方案。
- 默认配置偏向开发体验，公网部署前必须加固。
