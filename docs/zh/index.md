---
layout: home
title: 下一代 MQTT 平台

hero:
  name: NextMQTT
  text: 下一代 MQTT 平台
  tagline: 面向云、边缘与实时数据场景的下一代 .NET 原生 MQTT 平台，内置集群、管理 API 与实时控制台。
  image:
    src: /logo.svg
    alt: NextMQTT
  actions:
    - theme: brand
      text: 快速开始
      link: /zh/getting-started
    - theme: alt
      text: 架构设计
      link: /zh/architecture
    - theme: alt
      text: GitHub
      link: https://github.com/cn-cam/next-mqtt

features:
  - icon: 📡
    title: MQTT 3.1.1 与 5.0
    details: TCP、TLS、WebSocket、WSS 监听器，支持 QoS 0/1/2、保留消息、遗嘱、共享订阅、主题别名与会话过期。
  - icon: 🧩
    title: 集成与插件
    details: Webhook、NATS、Kafka、RabbitMQ 桥接（有界队列），以及可替换认证与授权的第一方插件宿主。
  - icon: 🔐
    title: 可加固的安全默认值
    details: PBKDF2 密码、HS256 JWT、MQTT 5 增强认证、主题 ACL、管理令牌、API Key，以及可显式指定私钥路径的 TLS。
  - icon: 🔁
    title: 自愈集群
    details: 签名 gossip 成员管理、跨节点路由、集群级共享订阅、新节点补齐保留消息、故障节点自动摘除。
  - icon: 🧭
    title: 运维优先
    details: 客户端、订阅、保留消息、发布控制台、插件与日志页面、集群拓扑与 Prometheus 指标集中呈现。
  - icon: 📦
    title: 部署省心
    details: 多架构镜像、Docker Compose、Kustomize 清单与 Helm Chart，内置探针与自动扩缩容。
  - icon: 🧼
    title: 独立实现
    details: 净室代码，Apache-2.0 许可，不包含任何第三方 Broker 的源码、配置格式、Logo 或商标。
---

## 最近更新

- **TLS 不再含糊**：证书、私钥路径与期望域名分开配置，自动下发中间证书；域名不被证书覆盖时启动即告警。
- **集群自愈**：新节点从对端补齐保留消息，成员变化即刻重算限流，长时间无心跳的节点会被真正摘除。
- **集成在规则之后**：被 `drop` 规则丢弃的消息不再进入 Webhook 或消息桥，改写后的主题对下游可见。
- **面板新增插件与日志**：插件宿主与有界日志缓冲区可以在面板查看，与客户端、主题、集群视图并列。
- **文档门禁**：`scripts/docs-coverage.ps1` 会在成员缺少注释时让构建失败，API 参考不会再退化。


## 为什么选择 NextMQTT？

NextMQTT 面向希望统一到 .NET 技术栈的团队：单一现代进程、明确的资源边界，
以及随 Broker 一起交付的运维能力。

- **单一 .NET 10 进程**，不依赖外部数据库或消息中间件。
- **全链路可观测**：连接、会话、inflight 窗口、订阅、保留消息与集群成员均可查看。
- **可预测的异步管线**：`System.IO.Pipelines` + 有界 Channel，用明确的背压替代无限队列。
- **经过 CI 验证的部署资产**，而不是文档里的草图。

## 一眼架构

```text
MQTT 客户端 ──TCP/TLS──┐
浏览器客户端 ──WS/WSS──┤
                       ├──► 监听器 ──► 编解码 ──► 会话 ──► 主题索引
管理控制台 ──HTTPS─────┘                                  │
                                                         ├──► 保留消息存储
                                                         └──► 集群路由
```

完整设计见[架构设计](/zh/architecture)，或直接前往[快速开始](/zh/getting-started)。

## 独立性

NextMQTT 与任何商业 MQTT Broker 厂商均无关联，也未获得其背书。仓库不包含
第三方 Broker 的源代码、文档文本、配置格式、Logo 或商标。详见
[TRADEMARKS.md](https://github.com/cn-cam/next-mqtt/blob/main/TRADEMARKS.md)。
