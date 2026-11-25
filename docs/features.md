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
| Shared subscriptions | ✅ | `$share/{group}/{filter}`, round-robin per node |
| Subscription options | ✅ | `noLocal`, `retainAsPublished`, retain handling |
| Topic aliases | ⚠️ | Inbound aliases supported; outbound aliases planned |
| Enhanced authentication (`AUTH`) | ✅ | Challenge/response with pluggable providers; built-in `nextmqtt-jwt` |
| Server-side receive-maximum enforcement | ⚠️ | Broker advertises and bounds its own inflight window |
| Client maximum packet size on delivery | ⚠️ | Inbound limit enforced; outbound packet trimming is not |
| MQTT over QUIC | ❌ | Research item |
| Bridge / rule engine / plugin SDK | ❌ | Roadmap |

## Sessions and delivery

| Capability | Status | Notes |
| --- | --- | --- |
| Clean start and session present | ✅ | MQTT 3.1.1 and 5.0 semantics |
| Persistent sessions | ✅ | In memory; see durability below |
| Session expiry sweep | ✅ | Background maintenance loop |
| QoS 1/2 inflight window | ✅ | Configurable per session |
| Packet-id allocation | ✅ | Skips ids already inflight |
| Offline queue bounds | ✅ | Oldest messages dropped and counted |
| Overlapping subscription aggregation | ✅ | One delivery per client at maximum QoS |
| Persistent state across restart | ⚠️ | Retained journal and periodic session snapshots; inflight QoS state is not persisted |
| Session backup and restore | ✅ | `GET`/`POST /api/v1/backup` export and import retained and session state |
| Graceful shutdown drain | ✅ | Waits up to `Server:DrainTimeoutSeconds` for inflight acknowledgements |
| Cross-node session migration | ❌ | Sessions are node-affine |

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
| Cross-node QoS guarantee | ❌ | Forwarding is best-effort HTTP |
| Cluster secret rotation without restart | ❌ | Single shared secret per deployment |
| Large filter-set routing | ⚠️ | Full filter snapshot per heartbeat, capped at 10,000 |

## Operations and management

| Capability | Status | Notes |
| --- | --- | --- |
| REST management API | ✅ | Clients, subscriptions, retained, publish, cluster, config |
| Vue 3 dashboard | ✅ | English/Chinese, dark/light, live polling |
| MQTT debugger | ✅ | Browser client over WebSocket: MQTT 3.1.1 and 5.0, QoS 0–2, MQTT 5 properties, subscriptions, packet trace, log export |
| Prometheus metrics | ✅ | `/metrics`, counters and gauges |
| JSON statistics | ✅ | 60-second publish series |
| Health and readiness | ✅ | `/healthz`, `/readyz` |
| Structured logs | ✅ | stdout, configurable levels |
| CLI | ✅ | `pub`, `sub`, `request`, `bench`, `hash-password` |
| Docker image | ✅ | Multi-arch, non-root, health check |
| Compose single node / 3-node cluster | ✅ | `docker-compose.yml`, `deploy/compose` |
| Kustomize manifests | ✅ | StatefulSet, PDB, HPA, Ingress, NetworkPolicy |
| Helm chart | ✅ | Probes, autoscaling, ingress, existing secret |
| Durable storage / backup | ❌ | Roadmap |
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

Current test count: 147 passing tests (63 core, 61 broker, 23 server)
integration).

## Extensibility

NextMQTT 0.1.x has extension **interfaces**, not a plugin ABI. Plugin packages
built for other MQTT brokers are not loaded, are not compatible, and are not
supported.

| Extension point | Interface | Purpose |
| --- | --- | --- |
| Client authentication | `IClientAuthenticator` | Replace or extend username/password and API-key checks |
| Topic authorisation | `ITopicAuthorizer` | Allow or deny publish/subscribe per client |
| Cluster routing | `IClusterRouter` | Replace the HTTP forwarding strategy |

Each is resolved from dependency injection, so an extension is a source change
plus a rebuild — no external runtime and no configuration schema of its own.
Out-of-process integrations use the versioned REST API (`/api/v1`) and the
cluster endpoints (`/internal/cluster`). A first-party extension host is on the
roadmap and needs its own ADR first (see
[ADR 0005](adr/0005-extension-interfaces.md)).

## Known limitations

- Sessions, queues and subscriptions are in memory; retained messages can be journaled.
- Cluster forwarding is best effort; shared subscriptions are coordinated across nodes.
- Durable sessions and bridges
  are not in this release.
- Extending behaviour means changing source; there is no plugin host yet.
- The default configuration is development-friendly; harden it before exposing
  the broker.
