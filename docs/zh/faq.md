# 常见问题

## 这是某个 Broker 的复刻吗？

不是。NextMQTT 是 OASIS MQTT 3.1.1/5.0 规范的独立净室实现，不包含任何第三方
MQTT Broker 的源码、文档文本、配置格式、Logo 或商标，也不声称与其存在关联或
获得认证。

## 可以用于生产吗？

协议、认证、ACL 与集群路径都有单元测试和 loopback 集成测试，镜像也在 CI 中构建。
持久化是可选的：保留消息、持久会话与会话副本都可以落盘，出站 inflight 确认由客户端
重放。把 Broker 当作记录系统之前，请先启用文件存储并规划卷。

## 为什么选择 .NET？

.NET 提供成熟的异步 Socket、`System.IO.Pipelines`、高性能 UTF-8 与密码学原语，
以及单文件部署模型。Broker 与 API 可以运行在同一个进程中，无需外部 VM 或数据库。

## 支持哪些客户端？

任何符合 MQTT 3.1.1/5.0 的客户端库。连接地址：

- `mqtt://host:1883`
- `mqtts://host:8883`（启用 TLS）
- `ws://host:8083/mqtt`
- `wss://host:8084/mqtt`（启用 HTTPS）

内置 CLI 也可用于冒烟测试。

## 单节点能承载多少连接？

取决于负载大小、QoS、订阅数量与硬件。设计目标是在中等 CPU 下承载数万空闲或低
速率客户端。请用 `nextmqtt bench` 结合真实负载测量，并关注离线队列与保留消息带来
的内存增长。代码中没有固定连接上限。

## 集群能保证 exactly-once 吗？

不能。MQTT QoS 只作用于客户端与 Broker 之间；跨节点转发是带去重的尽力而为 HTTP。
节点故障可能丢失发往该节点的消息，本地 QoS 1/2 语义仍然成立。

## 共享订阅能跨节点吗？

可以。节点在心跳中广告自己的共享组，每条消息在集群范围内只投递给组内一个成员，
而不是每个节点一次。详见[集群](/zh/clustering)。

## 状态存在哪里？

默认在内存中。设置 `Storage:Retained:Mode=File`、`Storage:Session:Mode=File` 与
`Storage:Replicas:Mode=File` 可把对应数据集合写入卷上的日志或快照；没有数据库或 WAL。
未启用文件存储时，重启会清空会话、队列与保留消息。配置与密钥保存在环境变量、
Compose 文件或 Kubernetes 对象中。

## 如何重置管理密码？

设置 `NEXTMQTT_ADMIN_PASSWORD` 后重启。若使用 `Admin:PasswordHash`：

```bash
dotnet run --project src/NextMqtt.Cli -- hash-password --password 'new-password'
```

## 重启后控制台令牌为什么失效？

未配置 `NEXTMQTT_ADMIN_TOKEN_SECRET` 时，Broker 会在启动时生成临时签名密钥。
请在所有节点设置稳定密钥。

## 关闭匿名后客户端为什么连不上？

需要至少一种 MQTT 凭据来源：

- `Authentication:Users` 中的本地用户，或
- `Authentication:Jwt:Secret` 对应的 JWT。

控制台管理密码不是 MQTT 凭据。

## 为什么 SUBSCRIBE 返回 `0x87`？

ACL 拒绝了该过滤器。订阅规则必须**覆盖**请求的过滤器，详见
[认证与 ACL](/authentication#semantics)。

## 保留消息为什么会消失？

- 使用空负载发布会删除保留消息；
- 消息过期时间已到；
- 发布所在节点在集群中不可用，未复制到其他节点；
- 被后续发布覆盖。

## 有遥测或许可证服务器吗？

没有。项目为 Apache-2.0，运行时不访问外部服务。

## 如何报告安全问题？

请使用 GitHub 私有漏洞报告，不要开公开 issue。参见
[SECURITY.md](https://github.com/cn-cam/next-mqtt/blob/main/SECURITY.md)。
