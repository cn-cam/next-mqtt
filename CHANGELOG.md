# Changelog

All notable changes to NextMQTT are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project adheres to [Semantic Versioning](https://semver.org/).

## [0.23.0] - 2026-11-26

### Added

- The dashboard ships an **API console** (System → API console) that can send
  every endpoint listed in `docs/api.md` with the signed-in session, including
  path editing, JSON bodies, status and duration. `scripts/api-surface-check.mjs`
  keeps the console and the documentation table in sync.
- `changelog.md` and `development-log.md` are generated for every locale, so the
  Chinese, Japanese and Korean sites no longer return 404 for those pages.
- The repository README is available in Chinese (default), English, Japanese and
  Korean, with explicit single-node and three-node deployment sections.
- `scripts/docs-consistency.mjs` fails CI when a translated page loses a
  heading, task-list item or table row relative to English. The rule is also
  recorded in the `docs-consistency` and `api-console` skills and in `AGENTS.md`.
- Session snapshots now include outbound messages that were waiting for an
  acknowledgement, with their absolute expiry. A restart with
  `Storage:Session:Mode=File` therefore replays them as queued messages instead
  of losing the window between sending a QoS 1/2 message and receiving its ack;
  a duplicate is possible.

### Changed

- Compose deployment documentation now names the host HTTPS ports 8086–8088
  instead of the container port 8084, and links to
  `deploy/compose/README.md`.
- Dependabot ignores major updates for the web workspace; the previous grouped
  PR bumped Pinia, Vue Router, TypeScript and Vite across major versions and
  failed both dashboard and image builds.

### Fixed

- Replica ordering now compares the push timestamp before the per-node version.
  A broker that restarted (and therefore restarted its version counter at zero)
  or a node that took ownership over no longer has its fresh replicas rejected
  by peers holding older, higher-version copies.
- The cluster leave log no longer writes the node id supplied by the internal
  request body, closing the CodeQL log-forging alert.
- The protocol and feature tables no longer describe outbound topic aliases as
  planned while the implementation and the same pages list them as complete.

## [0.22.0] - 2026-11-26

### Added

- Inbound QoS 2 publishes that are waiting for PUBREL now travel with the
  session: on a handover, a crash recovery from a replica, or a restart with
  session snapshots, the client can send PUBREL and the message is routed by the
  new owner instead of being lost.
- `Storage:Replicas:ShardCount` spreads replica snapshots over multiple files
  (default 16). Only shards whose content changed are rewritten, and a legacy
  single file is read at startup and replaced on the first save.
- Session replicas now include outbound messages that were waiting for an
  acknowledgement, with their absolute expiry, so a crash no longer loses the
  small window between sending a QoS 1/2 message and receiving its ack. Recovery
  replays them as queued messages, so a duplicate is possible.
- `Cluster:SessionReplicaBatchSize` (default 200) and
  `Cluster:SessionReplicaCapacity` (default 20,000) control how many sessions a
  node pushes per round and how many replicas it keeps; the documented recovery
  point now reflects the full sweep on large nodes instead of promising a fixed
  five seconds.
- Replica rounds refresh sessions with queued or unacknowledged messages first,
  then continue the rotating sweep over the rest, so the sessions that would
  lose data on a crash are the ones with the shortest recovery point.

### Fixed

- A session claim that cannot reach the recorded owner now falls back to the
  newest replica instead of aborting. Only a `404` counts as the owner
  answering that it does not hold the session; a transport failure is tracked
  separately, and other error responses do not resurrect a replica.

## [0.21.0] - 2026-11-26

### Added

- `Storage:Replicas` (`Mode`, `Path`) persists session replicas received from
  peers. With `Mode = File` a restarted node reloads them, so the cluster can
  still recover a session after every node restarted at once, or after the
  owner's own session file was lost. Snapshots are written at most every five
  seconds and once on shutdown.

### Changed

- A session claim now accepts a replica when the recorded owner answered the
  probe without holding the session, not only when that owner is dead. This
  covers a node that restarted with an empty or lost session store.
- Japanese and Korean guides caught up on TLS key paths, certificate domains,
  session replicas and the plugin and log endpoints.

## [0.20.0] - 2026-11-26

### Added

- `IPluginLifecycle`. A plugin can implement `Initialize(PluginContext)` and
  `Dispose()`; the host calls `Initialize` after construction (a failure skips
  that plugin) and `Dispose` before unloading the load context, so files,
  threads and sockets can be released deterministically.
- Plugin reload now re-reads the `Plugins` section from configuration. Editing
  `appsettings.json` and calling `POST /api/v1/plugins/reload` enables or
  disables plugins, changes the directory and switches the authenticator or
  authorizer without a restart.

### Fixed

- Plugins were only loaded when the dashboard first called `GET /api/v1/plugins`
  after the plugin runtime was introduced, so authentication ran on the built-in
  hook until someone opened the page. The runtime is now constructed before the
  server starts serving.

## [0.19.3] - 2026-11-26

### Fixed

- The session-owner probe answered with the client id instead of the node id.
  The reconcile logic still picked the right copy when the timestamps differed,
  but the tie-break compared unrelated strings, so two nodes that restarted at
  the same moment could both drop the session. The probe now reports the real
  node id.
## [0.19.2] - 2026-11-26

### Fixed

- Session replicas are now pushed by a dedicated loop instead of piggy-backing
  on the maintenance sweep, which runs every ten seconds and made a replica
  interval below that ineffective.
- Startup reconciliation now asks peers where a restored session currently
  lives, instead of looking for a replica. The replica lookup missed the case
  where the new owner was the only other node, and it could not break a tie when
  two restarted nodes both restored the same session. The newest copy wins, with
  a stable tie-break by node id, and the losing copy is removed before clients
  connect.
## [0.19.1] - 2026-11-26

### Fixed

- A node that restarted could resurrect sessions a peer had taken over while it
  was down, because the local session file knows nothing about the handover.
  Startup now compares each restored session with the cluster replicas and drops
  it when the recorded owner is a different, live node and the replica is not
  older. Without this, the same client could be served from two nodes and
  receive duplicates after reconnecting.
## [0.19.0] - 2026-11-26

### Added

- Session replicas. Every `Cluster:SessionReplicaSeconds` (default 5, `0`
  disables) a node pushes snapshots of its persistent sessions to its peers. If
  the owner dies before handing a session over, the node the client reconnects
  to restores it from the newest replica, provided the recorded owner is no
  longer alive. Replicas are held in memory and bounded to 20,000 entries.

### Changed

- A session claim now falls back to a replica when no live owner answers,
  instead of starting an empty session.

## [0.18.0] - 2026-11-26

### Added

- Cross-node session handover. A client that reconnects with `CleanStart=false`
  to a different node claims its session from the previous owner: the
  subscriptions and the offline queue move, `session present` is reported as 1,
  and the old connection is closed with 0x8E without publishing its will.
  Inbound QoS 2 handshakes are not transferred; the client retries with `DUP`.
- `POST /api/v1/plugins/reload` rescans the plugin directory, loads a new host
  and swaps authentication and authorisation atomically. The dashboard exposes
  the action and shows the reload count.
- Plugin status now reports the active hooks separately from the configured
  type names, so a missing or removed plugin is visible instead of implied.

### Changed

- Session affinity is no longer a documented limitation: sessions follow the
  client on reconnect, and the clustering guide describes what is and is not
  transferred.

## [0.17.1] - 2026-11-25

### Fixed

- The plugin page labelled the built-in authenticator and authorizer as not
  applied, which looked like a failure. The status tag now only appears when a
  plugin type name is configured, and it uses the dashboard's colour classes.

## [0.17.0] - 2026-11-25

### Changed

- Rate limits rescale as soon as a node joins or is removed, instead of waiting
  for the next maintenance sweep.
- Routing rules now run before integrations. A message discarded by a `drop`
  rule no longer reaches a webhook or a message bridge, and a message rewritten
  by a `rewrite` rule is forwarded under its new topic. Two broker-level tests
  cover both cases.

### Fixed

- A crashed node could stay in the membership table forever. Gossip responses
  relayed its row and every receiver stamped it with the current time; relayed
  rows now keep the observation time the sender reported, so removal after three
  timeouts actually happens. A regression test covers it.

### Added

- Dashboard views for the plugin host and recent logs, backed by
  `GET /api/v1/plugins` and `GET /api/v1/logs`. The log view filters by level
  and text and keeps the last 2,000 entries in memory.
- Generated documentation pages: `docs/changelog.md` from `CHANGELOG.md` and a
  development log from the commit history, both rebuilt with the docs site.
- `scripts/docs-i18n-check.ps1` records the coverage of every translated page
  and fails when a translation drifts further behind English.
- `CertificateKeyPath` and `Domain` for MQTT/TLS and HTTPS. A PEM file may hold
  the full chain, the intermediates are served to clients, and a `Domain` that
  the certificate does not cover is reported at startup instead of failing
  silently later.
- A joining node pulls the retained set from one peer, keeping the original
  timestamps, so retained values published while it was away are not lost.
- `scripts/docs-coverage.ps1` enforces the documentation rule in CI: every
  member under `src` needs a `<summary>`, members of publicly visible types
  also need `<remarks>`, and a member cannot carry two stacked doc blocks.
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

## [0.16.2] - 2026-11-25

### Fixed

- Webhooks, NATS, Kafka and RabbitMQ are invoked again on the origin node. A
  refactor of the routing path dropped the fan-out block, so integrations were
  built, configured and logged as connected but never received a message. A
  broker-level test now asserts the wiring.

## [0.16.1] - 2026-11-25

### Fixed

- The rate-limit drop path now counts the message as received before marking it
  dropped. A monitoring loop could previously observe the drop counter before the
  receipt counter, which made the topic-quota test flaky on CI.

## [0.16.0] - 2026-11-25

### Added

- RabbitMQ bridge through `RabbitMQ.Client` (Apache-2.0, ADR 0008): durable topic
  exchange, persistent messages, publisher confirms, bounded queue and automatic
  reconnect. Kafka and RabbitMQ can be enabled together.

## [0.15.0] - 2026-11-25

### Added

- Kafka bridge through `Confluent.Kafka` (Apache-2.0), the single approved
  runtime dependency (ADR 0007). Filtered, bounded and origin-only like the NATS
  bridge; a broken bootstrap address degrades to a warning.

## [0.14.0] - 2026-11-25

### Added

- NATS bridge: filtered publishes are forwarded over the NATS text protocol with
  a bounded queue and automatic reconnect, without adding a client dependency.

## [0.13.0] - 2026-11-25

### Added

- `rewrite` routing rules: the literal filter prefix is replaced with a target
  prefix, so `legacy/#` can feed `modern/#` without touching publishers.
  Invalid rewrite targets are rejected at startup and invalid results drop the
  message.

## [0.12.0] - 2026-11-25

### Added

- First-party plugin host: `plugin.json` manifests, contract-version checks and
  collectible load contexts that share the contract assemblies with the broker.
  Plugins can supply `IClientAuthenticator` and `ITopicAuthorizer`
  implementations, selected by full type name.

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

## [0.20.0] - 2026-11-26

### Added

- `IPluginLifecycle`. A plugin can implement `Initialize(PluginContext)` and
  `Dispose()`; the host calls `Initialize` after construction (a failure skips
  that plugin) and `Dispose` before unloading the load context, so files,
  threads and sockets can be released deterministically.
- Plugin reload now re-reads the `Plugins` section from configuration. Editing
  `appsettings.json` and calling `POST /api/v1/plugins/reload` enables or
  disables plugins, changes the directory and switches the authenticator or
  authorizer without a restart.

### Fixed

- Plugins were only loaded when the dashboard first called `GET /api/v1/plugins`
  after the plugin runtime was introduced, so authentication ran on the built-in
  hook until someone opened the page. The runtime is now constructed before the
  server starts serving.

## [0.19.3] - 2026-11-26

### Fixed

- The session-owner probe answered with the client id instead of the node id.
  The reconcile logic still picked the right copy when the timestamps differed,
  but the tie-break compared unrelated strings, so two nodes that restarted at
  the same moment could both drop the session. The probe now reports the real
  node id.
## [0.19.2] - 2026-11-26

### Fixed

- Session replicas are now pushed by a dedicated loop instead of piggy-backing
  on the maintenance sweep, which runs every ten seconds and made a replica
  interval below that ineffective.
- Startup reconciliation now asks peers where a restored session currently
  lives, instead of looking for a replica. The replica lookup missed the case
  where the new owner was the only other node, and it could not break a tie when
  two restarted nodes both restored the same session. The newest copy wins, with
  a stable tie-break by node id, and the losing copy is removed before clients
  connect.
## [0.19.1] - 2026-11-26

### Fixed

- A node that restarted could resurrect sessions a peer had taken over while it
  was down, because the local session file knows nothing about the handover.
  Startup now compares each restored session with the cluster replicas and drops
  it when the recorded owner is a different, live node and the replica is not
  older. Without this, the same client could be served from two nodes and
  receive duplicates after reconnecting.
## [0.19.0] - 2026-11-26

### Added

- Session replicas. Every `Cluster:SessionReplicaSeconds` (default 5, `0`
  disables) a node pushes snapshots of its persistent sessions to its peers. If
  the owner dies before handing a session over, the node the client reconnects
  to restores it from the newest replica, provided the recorded owner is no
  longer alive. Replicas are held in memory and bounded to 20,000 entries.

### Changed

- A session claim now falls back to a replica when no live owner answers,
  instead of starting an empty session.

## [0.18.0] - 2026-11-26

### Added

- Cross-node session handover. A client that reconnects with `CleanStart=false`
  to a different node claims its session from the previous owner: the
  subscriptions and the offline queue move, `session present` is reported as 1,
  and the old connection is closed with 0x8E without publishing its will.
  Inbound QoS 2 handshakes are not transferred; the client retries with `DUP`.
- `POST /api/v1/plugins/reload` rescans the plugin directory, loads a new host
  and swaps authentication and authorisation atomically. The dashboard exposes
  the action and shows the reload count.
- Plugin status now reports the active hooks separately from the configured
  type names, so a missing or removed plugin is visible instead of implied.

### Changed

- Session affinity is no longer a documented limitation: sessions follow the
  client on reconnect, and the clustering guide describes what is and is not
  transferred.

## [0.17.1] - 2026-11-25

### Fixed

- The plugin page labelled the built-in authenticator and authorizer as not
  applied, which looked like a failure. The status tag now only appears when a
  plugin type name is configured, and it uses the dashboard's colour classes.

## [0.17.0] - 2026-11-25

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

[Unreleased]: https://github.com/cn-cam/next-mqtt/compare/v0.23.0...HEAD
[0.23.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.23.0
[0.22.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.22.0
[0.21.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.21.0
[0.20.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.20.0
[0.19.3]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.19.3
[0.19.2]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.19.2
[0.19.1]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.19.1
[0.19.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.19.0
[0.18.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.18.0
[0.17.1]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.17.1
[0.17.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.17.0
[0.16.2]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.16.2
[0.16.1]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.16.1
[0.16.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.16.0
[0.15.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.15.0
[0.14.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.14.0
[0.13.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.13.0
[0.12.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.12.0
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
