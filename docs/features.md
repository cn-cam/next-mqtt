# Feature matrix

This page is the implementation contract for the current release. It separates
what is fully implemented from what is partial or planned, so deployment
decisions do not rely on marketing copy.

Legend: ✅ implemented · ⚠️ partial or with documented limits · ❌ not implemented

## Protocol and transport

| Capability | Status | Notes |
| --- | --- | --- |
| MQTT 3.1.1 (level 4) | ✅ | Codec, flags, CONNACK return codes |
| MQTT 5.0 (level 5) | ✅ | Core packet and property support |
| TCP listener | ✅ | Default port 1883 |
| TLS listener | ✅ | Optional PFX/PEM certificate, TLS 1.2/1.3 |
| WebSocket listener | ✅ | `/mqtt`, subprotocol `mqtt` |
| Secure WebSocket | ✅ | Kestrel HTTPS endpoint + `/mqtt` |
| QoS 0, 1 and 2 | ✅ | Both directions, retransmission, deduplication |
| Retained messages | ✅ | Wildcard retrieval, expiry, deletion |
| Will messages and will delay | ✅ | Suppressed on clean DISCONNECT |
| Session expiry | ✅ | MQTT 5 interval, DISCONNECT update |
| Session takeover | ✅ | Previous connection receives `0x8E` |
| Offline queue | ✅ | Bounded per persistent session |
| Shared subscriptions | ✅ | `$share/{group}/{filter}`, one member per group per message |
| Subscription options | ✅ | `noLocal`, `retainAsPublished`, retain handling |
| Topic aliases | ⚠️ | Inbound aliases supported; outbound aliases planned |
| Enhanced authentication (`AUTH`) | ✅ | Challenge/response with pluggable providers; built-in `nextmqtt-jwt` |
| Server-side receive-maximum enforcement | ⚠️ | Broker advertises and bounds its own inflight window |
| Client maximum packet size on delivery | ⚠️ | Inbound limit enforced; outbound packet trimming is not |
| MQTT over QUIC | ❌ | Research item |
| HTTP webhook bridge | ✅ | Per-rule bounded queues, retries, origin-only posting |
| Plugin host | ✅ | Manifest + collectible load context; authenticator/authorizer selection |
| Rule engine actions | ✅ | Topic-filtered `drop` and prefix `rewrite` before storage |
| NATS bridge | ✅ | Text-protocol publisher, bounded queue, auto-reconnect |
| Kafka bridge | ✅ | Confluent.Kafka producer, filtered and bounded like NATS |
| RabbitMQ bridge | ✅ | Durable topic exchange, publisher confirms, auto-reconnect |

## Sessions and delivery

| Capability | Status | Notes |
| --- | --- | --- |
| Clean start and session present | ✅ | MQTT 3.1.1 and 5.0 semantics |
| Persistent sessions | ✅ | In memory by default, optionally snapshotted to disk |
| Session expiry sweep | ✅ | Background maintenance loop |
| QoS 1/2 inflight window | ✅ | Configurable per session |
| Packet-id allocation | ✅ | Skips ids already inflight |
| Offline queue bounds | ✅ | Oldest messages dropped and counted |
| Overlapping subscription aggregation | ✅ | One delivery per client at maximum QoS |
| Persistent state across restart | ✅ | Retained journal plus session and replica snapshots; outbound inflight is replayed by the client |
| Session backup and restore | ✅ | `GET`/`POST /api/v1/backup` export and import retained and session state |
| Graceful shutdown drain | ✅ | Waits up to `Server:DrainTimeoutSeconds` for inflight acknowledgements |
| Cross-node session migration | ✅ | `CleanStart=false` claims the session from its owner or the newest replica |

## Security

| Capability | Status | Notes |
| --- | --- | --- |
| PBKDF2-SHA256 passwords | ✅ | Versioned hash format, 210k iterations |
| HS256 JWT | ✅ | `exp`, `nbf`, issuer, audience, roles |
| Anonymous access | ✅ | Explicit opt-in only |
| Topic ACL | ✅ | Ordered rules, identity placeholders, globs |
| Retained-read ACL checks | ✅ | Checked again at delivery time |
| TLS | ✅ | MQTT, dashboard/WSS |
| Dashboard token auth | ✅ | HS256 bearer tokens |
| API keys | ✅ | SHA-256 digest allow-list |
| Role-based dashboard permissions | ❌ | Single admin role today |
| Client connection/message rate limits | ✅ | Token buckets per remote address and per client |
| Topic-level publish quotas | ✅ | One token bucket per topic filter, shared across clients |
| Outbound topic aliases | ✅ | Server assigns aliases per connection within the client's Topic Alias Maximum |
| Subscription identifier quota | ✅ | `MaxSubscriptionIdentifiersPerClient` caps identified subscriptions per client |
| mTLS policy | ⚠️ | Optional client certificate request; platform validation |

## Cluster

| Capability | Status | Notes |
| --- | --- | --- |
| Gossip membership | ✅ | Heartbeats, seed discovery, suspect/dead states |
| Signed node transport | ✅ | HMAC-SHA256, five-minute timestamp window |
| Cross-node routing | ✅ | Forward only to nodes advertising a matching filter |
| Retained replication | ✅ | Origin timestamp, last-write-wins, deletion tombstones |
| Message deduplication | ✅ | Bounded seen-message cache |
| Loop prevention | ✅ | Remote publishes are not re-forwarded |
| Global shared subscriptions | ✅ | One node per shared group is chosen per message |
| Session handover on reconnect | ✅ | CleanStart=false claims the session from the previous node, including its offline queue and pending inbound QoS 2 |
| Durable session replicas | ✅ | Optional file store keeps replicas across a full-cluster restart |
| Cross-node QoS guarantee | ❌ | Forwarding is best-effort HTTP |
| Cluster secret rotation without restart | ❌ | Single shared secret per deployment |
| Large filter-set routing | ⚠️ | Full filter snapshot per heartbeat, capped at 10,000 |

## Operations and management

| Capability | Status | Notes |
| --- | --- | --- |
| REST management API | ✅ | Clients, subscriptions, retained, publish, cluster, logs, plugins, config |
| Vue 3 dashboard | ✅ | English/Chinese, dark/light, live polling, plugin and log views |
| MQTT debugger | ✅ | Browser client over WebSocket: MQTT 3.1.1 and 5.0, QoS 0–2, MQTT 5 properties, subscriptions, packet trace, log export |
| Plugin hot reload | ✅ | `POST /api/v1/plugins/reload` re-reads the settings, rescans the directory and swaps authentication and authorisation |
| Plugin lifecycle | ✅ | `IPluginLifecycle` receives `Initialize` and `Dispose` before unload |
| Prometheus metrics | ✅ | `/metrics`, counters and gauges |
| JSON statistics | ✅ | 60-second publish series |
| Health and readiness | ✅ | `/healthz`, `/readyz` |
| Structured logs | ✅ | stdout, configurable levels |
| CLI | ✅ | `pub`, `sub`, `request`, `bench`, `hash-password` |
| Docker image | ✅ | Multi-arch, non-root, health check |
| Compose single node / 3-node cluster | ✅ | `docker-compose.yml`, `deploy/compose` |
| Kustomize manifests | ✅ | StatefulSet, PDB, HPA, Ingress, NetworkPolicy |
| Helm chart | ✅ | Probes, autoscaling, ingress, existing secret |
| Durable storage | ✅ | Retained journal, session snapshots and session replicas |
| Distributed tracing | ❌ | Roadmap |

## Verification

The claims above are covered by the repository's tests and CI:

| Check | Where |
| --- | --- |
| Packet round trips and malformed input | `tests/NextMqtt.Core.Tests` |
| Topic matching and retained trie | `tests/NextMqtt.Core.Tests` |
| Sessions, QoS, queue, ACL, auth, cluster routing | `tests/NextMqtt.Broker.Tests` |
| Real TCP CONNECT/SUBSCRIBE/PUBLISH/QoS flows | `tests/NextMqtt.Server.Tests` |
| Dashboard typecheck and production build | CI `Dashboard` job |
| Docs build | CI `Documentation` job |
| Helm lint/render and Kustomize render | CI `Kubernetes manifests and Helm chart` job |
| Container build + health/login smoke | CI `Container image` job |
| CodeQL | `codeql.yml` |

Current test count: 215 passing tests (63 core, 109 broker, 43 server).

## Extensibility

NextMQTT has in-process extension interfaces and a plugin host. Plugins are
loaded from `plugin.json` manifests into a collectible context with contract
version 1; a manifest that names a missing assembly or a different contract
version is skipped.

| Extension point | Interface | Purpose |
| --- | --- | --- |
| Client authentication | `IClientAuthenticator` | Replace or extend username/password and API-key checks |
| Topic authorisation | `ITopicAuthorizer` | Allow or deny publish/subscribe per client |
| Cluster routing | `IClusterRouter` | Replace the HTTP forwarding strategy |
| Plugin lifecycle | `IPluginLifecycle` | Release resources before the load context is unloaded |

The interfaces are resolved from dependency injection, so an implementation can
also be compiled in. Plugins are an on-disk alternative that avoids a rebuild:
`Plugins:Authenticator` and `Plugins:Authorizer` name the type to activate, and
`POST /api/v1/plugins/reload` applies changes without a restart. Out-of-process
integrations use the versioned REST API (`/api/v1`) and the cluster endpoints
(`/internal/cluster`).

## Known limitations

- The outbound `inflight` acknowledgement state is not moved between nodes; those
  messages are replayed from the queue or a replica and may arrive twice. Inbound QoS 2
  publishes waiting for PUBREL move with the session.
- Replicas are only as fresh as `Cluster:SessionReplicaSeconds`; without
  `Storage:Replicas:Mode=File` they are lost when every node restarts.
- Cluster forwarding is best effort; shared subscriptions are coordinated across
  nodes.
- There is no cross-node ordering guarantee and no exactly-once delivery.
- Extending behaviour in-process means changing source and rebuilding; the
  plugin host is the runtime alternative.
- The default configuration is development-friendly; harden it before exposing
  the broker.
