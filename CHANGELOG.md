# Changelog

All notable changes to NextMQTT are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Outbound topic aliases: the server assigns aliases per connection inside the
  client's Topic Alias Maximum and sends later PUBLISH packets with an empty
  topic.
- `MaxSubscriptionIdentifiersPerClient` caps how many subscriptions per client
  may carry an MQTT 5 subscription identifier; excess filters get 0x97.
- Cluster-wide shared subscriptions: shared groups are advertised in heartbeats and
  each message is assigned to exactly one node that hosts the group, so members on
  different nodes no longer receive duplicate copies.
- Retained messages use the origin timestamp for last-write-wins merging, and
  deletions leave a tombstone so an older replica cannot resurrect a removed value.

## [0.11.0] - 2026-11-25

### Added

- Routing rules: publishes matching a `drop` filter are discarded before
  storage, delivery and forwarding, with tests and configuration docs.

## [0.10.0] - 2026-11-25

### Added

- HTTP webhook bridge: per-rule topic filters, bounded queues, three delivery
  attempts, and origin-only posting in a cluster.
- ADR 0006 scoping the first-party extension host.

### Fixed

- The server now registers the built-in `nextmqtt-jwt` enhanced authenticator
  and passes webhooks to the broker; both were missing from the host wiring.

## [0.9.0] - 2026-11-25

### Added

- `nextmqtt request` command built on MQTT 5 request/response: subscribes to a
  response topic, publishes with Response Topic and Correlation Data, and prints
  the matching reply.

## [0.8.0] - 2026-11-25

### Added

- MQTT 5 enhanced authentication: CONNECT-time challenge/response and re-authentication
  on live connections, with pluggable `IEnhancedAuthenticator` providers and a
  built-in `nextmqtt-jwt` provider.

## [0.7.0] - 2026-11-25

### Added

- Outbound topic aliases within the client's Topic Alias Maximum.
- `MaxSubscriptionIdentifiersPerClient` quota for identified subscriptions.
- Cluster-wide shared subscriptions with exactly one delivering node per group.
- Retained last-write-wins merging and deletion tombstones.

## [0.6.2] - 2026-11-25

### Fixed

- Test projects no longer generate XML documentation on any platform. The
  exclusion used a Windows path separator, so Linux CI tried to document test
  types, hit CS1591 (now an error) and failed the release build.

## [0.6.1] - 2026-11-25

### Changed

- Every public type and member in `Core`, `Broker`, `Server` and `Cli` now
  carries XML documentation; CS1591 is promoted to a build error so the backlog
  cannot return.

## [0.6.0] - 2026-11-24

### Added

- Cluster-aware rate limits: connection, per-client publish and topic quotas are
  divided across live nodes, so a cluster no longer multiplies the configured
  budget by its size.
- A `csharp-documentation` skill and an AGENTS.md rule requiring XML comments
  on every C# type and member, private members included.

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

[Unreleased]: https://github.com/cn-cam/next-mqtt/compare/v0.11.0...HEAD
[0.11.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.11.0
[0.10.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.10.0
[0.9.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.9.0
[0.8.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.8.0
[0.7.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.7.0
[0.6.2]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.6.2
[0.6.1]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.6.1
[0.6.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.6.0
[0.5.2]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.5.2
[0.5.1]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.5.1
[0.5.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.5.0
[0.4.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.4.0
[0.3.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.3.0
[0.2.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.2.0
[0.1.2]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.1.2
[0.1.1]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.1.1
[0.1.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.1.0
