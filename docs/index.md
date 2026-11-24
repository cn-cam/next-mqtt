---
layout: home

hero:
  name: NextMQTT
  text: Cloud-native MQTT platform
  tagline: An independent MQTT 3.1.1 and 5.0 broker built from scratch on .NET 10 and Vue 3, with clustering, a management API and a real-time dashboard.
  image:
    src: /logo.svg
    alt: NextMQTT
  actions:
    - theme: brand
      text: Get started
      link: /getting-started
    - theme: alt
      text: Architecture
      link: /architecture
    - theme: alt
      text: GitHub
      link: https://github.com/cn-cam/next-mqtt

features:
  - icon: 📡
    title: MQTT 3.1.1 and 5.0
    details: TCP, TLS, WebSocket and secure WebSocket listeners with QoS 0/1/2, retained messages, wills, shared subscriptions and session expiry.
  - icon: 🧭
    title: Operations first
    details: Live clients, subscriptions and retained state, a publish console, cluster topology and Prometheus metrics in one place.
  - icon: 🧩
    title: Cluster without a coordinator
    details: Signed gossip membership and cross-node routing. Start with one node and add more without reconfiguring clients.
  - icon: 📦
    title: Boring deployments
    details: Multi-architecture container image, Docker Compose, Kustomize manifests and a Helm chart with probes and autoscaling.
  - icon: 🔐
    title: Secure defaults you can tighten
    details: PBKDF2 passwords, HS256 JWT, topic ACLs, scoped admin tokens, API keys and TLS.
  - icon: 🧼
    title: Independent by design
    details: Clean-room code, Apache-2.0, no third-party broker source, configuration formats, logos or trademarks.
---

## Why another broker?

Most MQTT deployments fail operationally, not at the protocol layer. NextMQTT
focuses on the things that make a broker maintainable in production:

- **A single .NET process** with no external database, Erlang VM or message
  broker dependency.
- **Observable state** at every layer: connections, sessions, inflight windows,
  subscriptions, retained messages and cluster membership.
- **Predictable async pipelines** built on `System.IO.Pipelines` and bounded
  channels, with explicit back-pressure instead of unbounded queues.
- **Deployment assets that are tested in CI**, not sketched in a wiki.

## At a glance

```text
MQTT clients ──TCP/TLS──┐
Browser clients ──WS/WSS─┤
                         ├──► listeners ──► codec ──► sessions ──► topic index
Management UI ──HTTPS───┘                                  │
                                                           ├──► retained store
                                                           └──► cluster router
```

Read [Architecture](/architecture) for the component model, data structures
and failure behaviour, or jump straight to the [quick start](/getting-started).

## Independence

NextMQTT is not affiliated with, endorsed by, or derived from any commercial
MQTT broker. The repository contains no third-party broker source code,
documentation text, configuration format, logo or trademark. See
[TRADEMARKS.md](https://github.com/cn-cam/next-mqtt/blob/main/TRADEMARKS.md).
