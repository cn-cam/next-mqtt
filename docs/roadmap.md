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

- [x] File-backed retained store with an append-only journal (0.4.0)
- [x] Journal compaction at startup and when the file grows (0.4.0)
- [x] Durable session snapshots with resumed queues and subscriptions (0.5.0)
- [ ] SQLite and PostgreSQL providers
- [x] Backup and restore of retained and session state (0.5.0)
- [x] Graceful drain that waits for inflight acknowledgements (0.5.0)
- [x] Per-client and per-IP connection rate limits (0.3.0)
- [x] Message-rate quotas per client (0.3.0)
- [x] Topic-level publish quotas (0.5.0)
- [ ] OpenTelemetry traces for publish and delivery paths

## 0.3 — Protocol depth

- [x] Enhanced authentication (`AUTH`) with pluggable providers (0.8.0)
- [ ] Outbound topic aliases
- [x] Response-topic request/response helpers (0.9.0)
- [ ] MQTT over QUIC experiment
- [ ] Global shared-subscription coordination across nodes
- [x] Cluster-aware retained tombstones and last-write-wins conflict resolution (0.7.0)
- [ ] Subscription identifier limits per client

## 0.4 — Integrations

- [x] HTTP webhook bridge with filter rules (0.10.0)
- [x] NATS bridge module (0.14.0)
- [x] NATS bridge module (0.14.0)
- Kafka bridge dropped: it needs a client dependency this project does not take
- [ ] Rule engine for topic routing, filtering and enrichment
- [x] First-party plugin host with manifest and collectible loading (0.12.0)

  Scoped as a first-party extension host: a versioned contract, an explicit
  lifecycle and a manifest this project defines, not a foreign plugin API. It
  needs its own ADR before code lands (see
  [ADR 0005](adr/0005-extension-interfaces.md)). Until then, extension points are
  the interfaces listed in [Features](features.md#extensibility).
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
