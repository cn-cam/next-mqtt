# Roadmap

The roadmap is intent, not a contract. Priorities follow production feedback and
security needs.

## 0.1 — Foundation

- [x] MQTT 3.1.1 and 5.0 over TCP, TLS, WebSocket and secure WebSocket
- [x] QoS 0/1/2, retained messages, wills, session expiry
- [x] Shared subscriptions and MQTT 5 subscription options
- [x] Password, JWT and anonymous authentication
- [x] Ordered topic ACL with identity placeholders
- [x] Gossip membership and signed cross-node routing
- [x] REST API and Vue 3 dashboard
- [x] CLI, Docker, Compose, Kubernetes and Helm
- [x] Prometheus metrics, health probes and structured logs

## 0.2 — Durability and operations

- [ ] Pluggable session and retained stores
- [ ] File-based append-only store with compaction
- [ ] Optional SQLite and PostgreSQL providers
- [ ] Session and retained backup/restore procedures
- [ ] Graceful node drain that waits for inflight acknowledgements
- [ ] Per-client and per-IP connection rate limits
- [ ] Message-rate quotas and topic-level quotas
- [ ] OpenTelemetry traces for publish and delivery paths

## 0.3 — Protocol depth

- [ ] Enhanced authentication (`AUTH`) with pluggable providers
- [ ] Outbound topic aliases
- [ ] Response-topic request/response helpers
- [ ] MQTT over QUIC experiment
- [ ] Global shared-subscription coordination across nodes
- [ ] Cluster-aware retained tombstones and conflict resolution
- [ ] Subscription identifier limits per client

## 0.4 — Integrations

- [ ] HTTP webhook bridge
- [ ] Kafka and NATS bridge modules
- [ ] Rule engine for topic routing, filtering and enrichment
- [ ] Plugin SDK for .NET extensions
- [ ] Schema registry hooks for payload validation
- [ ] Multi-tenant namespaces

## Later

- [ ] Read replicas and follower routing
- [ ] Hierarchical cluster routing for very large filter sets
- [ ] MQTT-SN gateway
- [ ] Edge store-and-forward mode
- [ ] Managed cloud offering documentation (not a separate product)

## Explicit non-goals

- Compatibility with a third-party broker's private plugin API.
- Reusing a third-party broker's configuration files or dashboards.
- Copying source, tests or documentation from another implementation.
- Claiming certification or endorsement by a broker vendor.

## How to influence the roadmap

Open a discussion with:

- the workload (clients, message rate, payload sizes, retention),
- deployment target (single node, Kubernetes, edge),
- the operational pain you need to remove,
- any compliance constraints.

Security fixes always take precedence over feature work.
