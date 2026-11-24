# Changelog

All notable changes to NextMQTT are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project adheres to [Semantic Versioning](https://semver.org/).

## [0.5.2] - 2026-11-24

### Fixed

- `nextmqtt sub` no longer drops messages that arrive before SUBACK, so a
  resumed session shows its queued messages.

## [0.5.1] - 2026-11-24

### Fixed

- The shutdown drain now runs in the TCP listener before it cancels
  connections, so inflight QoS traffic actually settles before sockets close.

## [0.5.0] - 2026-11-24

### Added

- Durable persistent sessions: periodic atomic snapshots of identity, expiry,
  subscriptions and offline queues (`Storage:Session`), restored on startup.
- `GET` and `POST /api/v1/backup` to export and import retained messages and
  persistent sessions.
- Graceful shutdown drain that waits for inflight QoS acknowledgements
  (`Server:DrainTimeoutSeconds`).
- Topic-level publish quotas shared across clients (`RateLimits:Topics`).

### Changed

- Retained and session stores share one `StorageMode` enum.

## [0.4.0] - 2026-11-24

### Added

- File-backed retained-message store: append-only JSON-lines journal with
  startup replay, expiry filtering, torn-line tolerance and compaction
  (`Storage:Retained`).
- Cluster Compose nodes mount a data volume so the retained journal survives
  container restarts.

### Changed

- Retained messages can survive a restart; sessions, queues and subscriptions
  are still in memory.

## [0.3.0] - 2026-11-24

### Added

- Per-address CONNECT rate limits and per-client PUBLISH rate limits, both
  token buckets with configurable burst and sustained rate (`RateLimits`).
- Rate-limit counters: `nextmqtt_connections_rate_limited_total` and
  `nextmqtt_publishes_rate_limited_total`.

### Changed

- Rate-limited publishes are acknowledged but not routed, so publishers do not
  retransmit. MQTT 5 QoS 2 rejections carry `0x96 Message rate too high`.

## [Unreleased]

### Added

- Complete Japanese and Korean documentation sites, plus the missing Chinese
  reference pages and a four-language VitePress locale switcher.
- Docker Hub publishing from the release pipeline and a main-branch `edge`
  image workflow.

### Planned

- Durable session and retained-message stores.
- Enhanced authentication (`AUTH`) providers.
- Global shared-subscription coordination.

## [0.2.0] - 2026-11-24

### Added

- MQTT debugger in the dashboard: MQTT 3.1.1 and 5.0 over WebSocket with
  subscriptions, QoS 0-2 publishing, MQTT 5 properties, wills, packet trace and
  JSON log export.
- `nextmqtt bench-connections` connection benchmark with accept rate, failure
  count and a hold window.
- Measured throughput and connection numbers in the operations guide.
- ADR 0005 and an English ADR index.

### Changed

- Docker Hub publishing is skipped when the login fails, so releases still
  publish to GHCR.
- Cluster composition sets a shared dashboard token secret so one sign-in works
  across nodes.

### Fixed

- The cluster heartbeat loop no longer stops when a peer cannot be resolved,
  which previously froze membership until restart.

## [0.1.2] - 2026-11-24

### Added

- Feature matrix covering implemented, partial and planned capabilities.
- CLI reference in English and Chinese.
- Restrictive Content-Security-Policy for the dashboard and API responses.
- Bilingual navigation links for features and CLI documentation.

### Changed

- Repositioned the project as a new .NET 10 MQTT platform rather than a
  comparison or compatibility layer.
- Corrected Helm examples to use the actual chart values and the Kubernetes
  quick-start path.
- Clarified that dashboard administration uses token/API-key auth, not RBAC.

### Fixed

- Removed the literal letter list from the public compliance policy; the
  prohibited token is now only reconstructed by the scanner.
- Aligned the web dependency lockfile so the repository contains no prohibited
  brand prefix.

## [0.1.1] - 2026-11-24

### Fixed

- Anonymous MQTT clients are no longer accepted when `AllowAnonymous` is false.
  Anonymous access now requires an explicit opt-in.
- Cluster shutdown is idempotent when the host stops and disposes the broker.
- ACL wildcard rules no longer cover leading-`$` topics such as `$SYS/#`.
- Certificate loading uses the non-obsolete `X509CertificateLoader`.
- Documentation titles no longer repeat the product name.
- CLI nullability warnings and the dashboard publish history are cleaned up.

## [0.1.0] - 2026-11-24

### Added

- MQTT 3.1.1 and 5.0 codec with strict validation and malformed-packet tests.
- TCP, TLS, WebSocket and secure WebSocket listeners.
- QoS 0/1/2 in both directions, inflight windows and retransmission.
- Persistent sessions, session expiry, takeover and offline queues.
- Retained messages with message expiry and wildcard retrieval.
- Wills with delay, suppression on clean disconnect and replica support.
- Shared subscriptions with per-group round-robin.
- MQTT 5 subscription options, properties and inbound topic aliases.
- PBKDF2 password authentication, HS256 JWT and anonymous access.
- Ordered topic ACL with identity placeholders and superusers.
- Signed gossip cluster membership and cross-node publish routing.
- Retained-message replication across cluster nodes.
- REST management API with paginated clients, subscriptions and retained state.
- Vue 3 + TypeScript dashboard with English/Chinese UI and dark/light themes.
- CLI with `pub`, `sub`, `bench` and `hash-password` commands.
- Multi-architecture container image with health check.
- Docker Compose single-node and three-node cluster definitions.
- Kustomize manifests with StatefulSet, PDB, HPA, Ingress and NetworkPolicy.
- Helm chart with configurable auth, cluster, resources and ingress.
- Prometheus metrics exposition and JSON statistics.
- VitePress documentation site with English and Chinese guides.
- GitHub Actions for CI, CodeQL, releases and GitHub Pages.
- Mechanical brand/trademark compliance scanner and repository policy.

[Unreleased]: https://github.com/cn-cam/next-mqtt/compare/v0.5.2...HEAD
[0.5.2]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.5.2
[0.5.1]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.5.1
[0.5.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.5.0
[0.4.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.4.0
[0.3.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.3.0
[0.2.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.2.0
[0.1.2]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.1.2
[0.1.1]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.1.1
[0.1.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.1.0
