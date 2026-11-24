---
layout: home
title: 云原生 MQTT 平台

hero:
  name: NextMQTT
  text: 云原生 MQTT 平台
  tagline: 基于 .NET 10 与 Vue 3 从零实现的独立 MQTT 3.1.1 / 5.0 Broker，内置集群、管理 API 与实时控制台。
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
    details: TCP、TLS、WebSocket、WSS 监听器，支持 QoS 0/1/2、保留消息、遗嘱、共享订阅与会话过期。
  - icon: 🧭
    title: 运维优先
    details: 客户端、订阅、保留消息、发布控制台、集群拓扑与 Prometheus 指标集中呈现。
  - icon: 🧩
    title: 无中心集群
    details: 签名 gossip 成员管理与跨节点路由。从单机开始，随时扩容，无需改动客户端。
  - icon: 📦
    title: 部署省心
    details: 多架构镜像、Docker Compose、Kustomize 清单与 Helm Chart，内置探针与自动扩缩容。
  - icon: 🔐
    title: 可加固的安全默认值
    details: PBKDF2 密码、HS256 JWT、主题 ACL、管理令牌、API Key 与 TLS。
  - icon: 🧼
    title: 独立实现
    details: 净室代码，Apache-2.0 许可，不包含任何第三方 Broker 的源码、配置格式、Logo 或商标。
---

## 为什么是 NextMQTT？

MQTT 系统的问题往往不在协议层，而在运维层。NextMQTT 关注让 Broker 在
生产环境中可持续维护的能力：

- **单一 .NET 进程**，不依赖外部数据库、Erlang VM 或消息中间件。
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
