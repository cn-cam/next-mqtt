---
layout: home
title: Next-generation MQTT platform

hero:
  name: NextMQTT
  text: Next-generation MQTT platform
  tagline: A next-generation, .NET-native MQTT platform for cloud, edge and real-time data — with clustering, a management API and a dashboard built in.
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
    details: TCP, TLS, WebSocket and secure WebSocket listeners with QoS 0/1/2, retained messages, wills, shared subscriptions, topic aliases and session expiry.
  - icon: 🧩
    title: Integrations and plugins
    details: Webhooks, NATS, Kafka and RabbitMQ bridges with bounded queues, plus a first-party plugin host for custom authentication and authorisation.
  - icon: 🔐
    title: Secure defaults you can tighten
    details: PBKDF2 passwords, HS256 JWT, MQTT 5 enhanced authentication, topic ACLs, scoped admin tokens, API keys and TLS with an explicit key path.
  - icon: 🔁
    title: Self-healing cluster
    details: Signed gossip membership, cross-node routing, cluster-wide shared subscriptions, retained-state catch-up on join and automatic removal of crashed nodes.
  - icon: 🧭
    title: Operations first
    details: Live clients, subscriptions, retained state, a publish console, plugin and log views, cluster topology and Prometheus metrics in one place.
  - icon: 📦
    title: Boring deployments
    details: Multi-architecture container image, Docker Compose, Kustomize manifests and a Helm chart with probes and autoscaling.
  - icon: 🧼
    title: Independent by design
    details: Clean-room code, Apache-2.0, no third-party broker source, configuration formats, logos or trademarks.
---

## Recent additions

- **TLS without surprises**: certificate, private key path and expected domain are
  separate settings, the intermediate chain is served, and a certificate that
  does not cover the configured name is reported at startup.
- **Cluster self-healing**: a joining node pulls the retained set from a peer,
  limits rescale the moment membership changes, and a node that stays silent is
  removed instead of being kept alive by gossip.
- **Integrations after routing rules**: a message discarded by a `drop` rule no
  longer reaches a webhook or a bridge, and a rewritten message is forwarded
  under its new topic.
- **Dashboard plugins and logs**: the plugin host and a bounded log buffer are
  visible from the dashboard, next to the existing client, topic and cluster
  views.
- **Documentation gate**: `scripts/docs-coverage.ps1` fails the build when a
  member has no comment, so the reference cannot drift.


## A new foundation for MQTT

NextMQTT is built for teams standardizing on .NET: one modern process, explicit
resource limits and operations tooling that ships with the broker.

- **A single .NET 10 process** with no external database or message middleware.
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
