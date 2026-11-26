# FAQ

## NextMQTT 是现有产品的分支或重写吗？

不是。它是对 OASIS MQTT 3.1.1 与 5.0 规范的独立清室实现。代码库不包含其他 MQTT
Broker 的源代码、文档文字、配置格式、Logo 或商标，也不声称任何关联或认证。

## 可以用于生产吗？

协议、认证、ACL 与集群路径都有单元测试和 loopback 集成测试覆盖，镜像也在 CI 中
构建。持久化是可选项：保留消息、持久会话与会话副本都可以落盘，出站 inflight 确认由
客户端重放。把 Broker 当作记录系统之前，请先启用文件存储并规划卷。

## 为什么选择 .NET？

.NET 提供成熟的异步 Socket、`System.IO.Pipelines`、高性能 UTF-8 与密码学原语，以及
单文件部署模型。Broker 与 API 在一个进程中运行，不需要 VM 或外部数据库。

## 哪些 MQTT 客户端可用？

任何符合 MQTT 3.1.1 或 5.0 的客户端库。连接地址：

- `mqtt://host:1883`
- `mqtts://host:8883`（启用 TLS 时）
- `ws://host:8083/mqtt`
- `wss://host:8084/mqtt`（配置 HTTPS 时）

内置 `nextmqtt` CLI 也是客户端，适合冒烟测试。

## 单节点能承载多少连接？

取决于负载大小、QoS、订阅数和硬件。设计目标是在适度的 CPU 下，每节点支持数万个空闲
或低频客户端。请用 `nextmqtt bench` 按自己的负载测量，并观察离线队列与保留消息占用
的内存。代码中没有固定连接上限。

## 集群保证 exactly-once 吗？

不保证。MQTT QoS 只作用于客户端与 Broker 之间。跨节点转发是带去重缓存的尽力而为
HTTP。对端故障时，发往它的消息可能丢失。本地 QoS 1/2 语义仍然保留。

## 共享订阅能跨节点吗？

可以。节点在心跳中广告共享组，每条消息在集群范围内只分配给组内一个成员，而不是每个
节点一次。详见[集群](/zh/clustering)。

## 状态存在哪里？

默认在内存中。设置 `Storage:Retained:Mode=File`、`Storage:Session:Mode=File` 与
`Storage:Replicas:Mode=File` 可把对应数据集合写入卷上的日志或快照；没有数据库或 WAL。
未启用文件存储时，重启会清空会话、队列与保留消息。配置与密钥保存在环境变量、
Compose 文件或 Kubernetes 对象中。

## 如何重置管理密码？

设置 `NEXTMQTT_ADMIN_PASSWORD` 后重启。若使用 `Admin:PasswordHash`，生成新哈希：

```bash
dotnet run --project src/NextMqtt.Cli -- hash-password --password 'new-password'
```

## 重启后控制台令牌为什么失效？

没有配置 `NEXTMQTT_ADMIN_TOKEN_SECRET`，Broker 生成了临时签名密钥。请在每个节点设置
固定密钥。

## 关闭匿名后客户端为什么连不上？

至少需要一个 MQTT 凭据来源：

- `Authentication:Users` 中的本地用户，或
- `Authentication:Jwt:Secret` 中的 JWT 密钥。

控制台管理密码不是 MQTT 凭据。

## SUBSCRIBE 为什么返回 `0x87`？

ACL 拒绝了该过滤器。订阅规则必须**覆盖**请求的过滤器。详见
[认证与 ACL](/zh/authentication)。

## 保留消息为什么消失了？

- 以空负载发布（删除保留状态）。
- 消息过期时间已到。
- 在对端离线时发布，未复制成功。
- 被后续发布替换。

## 能从其他来源访问控制台吗？

把来源加入 `Server:Http:AllowedOrigins`。Vite 开发服务器默认是
`http://localhost:5173`。CORS 不能替代认证。

## 容器端口如何配置？

使用环境变量，例如：

```bash
NEXTMQTT_SERVER__HTTP__PORT=9090
NEXTMQTT_SERVER__TCP__PORT=11883
```

记得在 Docker/Kubernetes 中发布相同的容器端口。

## 是否需要遥测或许可证密钥？

没有遥测、没有许可证服务器、运行时没有外部依赖。项目使用 Apache-2.0。

## 如何报告安全问题？

使用 GitHub 私有漏洞报告，不要开公开 issue。见
[SECURITY.md](https://github.com/cn-cam/next-mqtt/blob/main/SECURITY.md)。
