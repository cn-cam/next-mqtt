# Architecture

NextMQTT is a modular monolith: one process, a small number of well-defined
components, and explicit boundaries between the data plane and the control
plane. This page describes the design, the data structures, the delivery path
and the trade-offs.

## Design principles

1. **Data plane isolation.** MQTT connections, sessions and routing run
   independently of the HTTP API and dashboard. A slow dashboard request cannot
   block a publish.
2. **Bounded everything.** Per-session inflight windows, offline queues, packet
   size limits and a bounded outbound channel per connection. Back-pressure is
   visible through dropped-message metrics instead of silent memory growth.
3. **One owner per session.** A client identifier is owned by exactly one node.
   Session takeover is explicit and race-free within a node.
4. **Control plane is replaceable.** The REST API and dashboard use the same
   broker facade as the protocol engine; no private back doors.
5. **Clean-room independence.** Behaviour follows the OASIS MQTT
   specifications, not the internals of any other MQTT product.

## Component map

```text
                         ┌─────────────────────────────────────────────┐
   MQTT/TCP, MQTT/TLS    │                 NextMqtt.Server             │
   ────────────────────► │  Kestrel (API, dashboard, WebSocket /mqtt)   │
                         │  TCP/TLS listeners (BackgroundService)       │
                         └──────────────────────┬──────────────────────┘
                                                │ one ClientConnection per socket
                         ┌──────────────────────▼──────────────────────┐
                         │                 NextMqtt.Broker             │
                         │                                             │
                         │  ClientConnection ──► BrokerSession         │
                         │        │                    │               │
                         │        ▼                    ▼               │
                         │  MqttBroker.RouteAsync ──► SubscriptionIndex│
                         │        │                    │               │
                         │        ├──► RetainedMessageStore            │
                         │        └──► ClusterRouter ──► peers         │
                         │                                             │
                         │  ClientAuthenticator / TopicAuthorizer      │
                         │  BrokerMetrics / RateTracker                │
                         └──────────────────────┬──────────────────────┘
                                                │
                         ┌──────────────────────▼──────────────────────┐
                         │                  NextMqtt.Core              │
                         │  MQTT codec, topic filters, topic trie      │
                         └─────────────────────────────────────────────┘
```

### `NextMqtt.Core`

- MQTT 3.1.1/5.0 packet models, decoder, encoder and `PipeReader` frame reader.
- Strict UTF-8 and property validation.
- Topic filter parsing/matching and the generic topic trie used by both
  subscription and retained indexes.
- No networking, no logging, no third-party dependencies.

### `NextMqtt.Broker`

- `ClientConnection`: one socket, protocol state machine, keep-alive, topic
  aliases, outbound channel and graceful shutdown.
- `BrokerSession`: persistent session state, inflight windows, offline queue,
  inbound QoS 2 deduplication.
- `MqttBroker`: authentication, session attach/takeover, routing, retained
  messages, administration and maintenance.
- `SubscriptionIndex`, `RetainedMessageStore`, `TopicAuthorizer`,
  `ClientAuthenticator`, `ClusterRouter`, metrics.

### `NextMqtt.Server`

- Hosts Kestrel for REST, dashboard static files and MQTT over WebSocket.
- Runs raw TCP and TLS listeners.
- Maps the signed internal cluster API.
- Bootstraps admin credentials and serves the Vue bundle.

### `NextMqtt.Cli`

- `pub`, `sub`, `bench` and `hash-password` commands using the shared codec.
- Useful for smoke tests, cluster checks and load experiments.

## Connection lifecycle

1. The listener accepts a socket and creates a `ClientConnection`.
2. The first packet must be CONNECT, within `Broker:ConnectTimeout`.
3. The client identifier is validated or generated. Zero-length ids are
   assigned by the broker when allowed.
4. `ClientAuthenticator` tries password auth, then a JWT in the password field,
   then anonymous access if enabled.
5. `MqttBroker.AttachSessionAsync` serialises session attachment for the node:
   - clean start or a protocol change discards the old session;
   - otherwise the existing session is resumed (`session present = 1`);
   - when the session is owned by another node, the broker claims it from that
     node, or falls back to the newest replica if the owner is gone or no longer
     holds it;
   - a live previous connection receives DISCONNECT `0x8E` (session taken over).
6. CONNACK is written directly, then the outbound writer starts and inflight
   messages plus the offline queue are flushed.
7. The read loop dispatches packets until DISCONNECT, keep-alive timeout, a
   protocol error or socket failure.
8. Cleanup detaches the connection, schedules the will, expires or preserves the
   session and releases subscriptions when the session ends.

## Data structures

### Topic trie

`TopicTrie<TValue>` is a prefix tree keyed by topic level.

- Subscription matching walks a topic and collects values at the exact node,
  the `+` child and the `#` child.
- Retained lookup walks a filter: `#` collects a subtree, `+` iterates children,
  and literals descend.
- The `$`-topic rule is enforced at the root: wildcards never match a leading
  `$SYS`-style level.
- Removal prunes empty branches; a bucket at a node can hold multiple values.

### Subscription index

- Normal subscriptions live in one trie keyed by the effective filter.
- Shared subscriptions are grouped by `(group, filter)`; each group is a
  round-robin membership list.
- A per-client dictionary provides O(1) lookup and cleanup on disconnect.
- A publish produces a list of `(subscription, sharedGroup)` matches; the
  broker then aggregates matches per client to avoid duplicate delivery from
  overlapping filters.

### Sessions

`BrokerSession` owns:

| Field | Purpose |
| --- | --- |
| `_inflight` | Outbound QoS 1/2 packets waiting for PUBACK/PUBREC/PUBCOMP |
| `_queue` | Bounded offline queue for persistent sessions |
| `_inboundQos2` | Packet ids awaiting PUBREL, preventing duplicate delivery |
| `_nextPacketId` | Monotonic packet identifier allocator, skipping inflight ids |
| `ExpiresAt` | Absolute session expiry, evaluated by the maintenance loop |

All session mutations take a per-session lock. Delivery into the outbound
channel is non-blocking; a full channel is recorded as a drop rather than
stalling the router.

### Retained store

Retained messages are stored by exact topic in a topic trie. Retrieval uses the
same wildcard semantics as subscriptions. A retained publish with an empty
payload deletes the entry. Message expiry is converted to an absolute deadline
and enforced on read and during maintenance.

## Publishing path

```text
PUBLISH ──► validate topic/alias ──► ACL publish
        ──► retained update (if retain)
        ──► subscription trie match
        ──► aggregate per client (max QoS, RAP, subscription ids)
        ──► session queue/inflight ──► connection outbound channel
        ──► cluster forward (normal subscriptions + retained replication)
```

QoS on delivery is `min(publish QoS, subscription QoS)`. The retain flag is
cleared for live forwarding unless the subscription sets
`retainAsPublished`. A subscription identifier from SUBSCRIBE is attached to
delivered messages for MQTT 5 clients.

## QoS state machines

**Outbound QoS 1:** PUBLISH → PUBACK. Unacknowledged packets are retransmitted
with DUP=1 after `Broker:RetransmitInterval`.

**Outbound QoS 2:** PUBLISH → PUBREC → PUBREL → PUBCOMP. PUBREL is retransmitted
until PUBCOMP.

**Inbound QoS 2:** PUBLISH is stored by packet id and PUBREC is sent. The
message is routed only when PUBREL arrives, then PUBCOMP is sent. A repeated
PUBLISH with the same id does not create a second delivery.

## Clustering

Nodes form a mesh over signed HTTP:

- Every node advertises its **normal** subscription filters in a heartbeat.
- Membership is gossip-based: responses include the responder's member table,
  so a node only needs one reachable seed.
- A publish is forwarded only to live nodes with a matching advertised filter.
  Retained publishes are replicated to every live node so retained state
  converges.
- Session snapshots are pushed to peers every `SessionReplicaSeconds`; a new
  node claims a session from its owner, and uses the newest replica only when
  the owner is not alive or reports that it does not hold the session.
- Message ids provide deduplication; remote publishes are never re-forwarded,
  which prevents routing loops.
- Shared subscriptions are coordinated across the cluster: for each message one
  node in the shared group is selected, so a message is not delivered once per
  node. See [Clustering](/clustering#delivery-semantics).

The internal API is protected with HMAC-SHA256 over a timestamp and the raw
body, with a five-minute clock-skew window. The shared secret is required when
clustering is enabled.

## Concurrency and back-pressure

- One read loop and one write loop per connection.
- `System.IO.Pipelines` reassembles frames without per-packet allocations for
  the framing layer.
- A bounded channel (1024 frames) decouples routing from socket writes.
- Per-session inflight windows bound unacknowledged QoS traffic.
- The maintenance loop prunes expired retained messages and sessions and
  triggers retransmissions.
- Metrics expose drops and retransmissions, so overload is observable.

## Durability and failure model

| Event | Behaviour |
| --- | --- |
| Client disconnects | Session preserved if it has a non-zero expiry / clean start is false |
| Broker restarts | Retained messages and persistent sessions come back from their file stores when enabled; without them they are lost |
| Node crashes in a cluster | Peers mark it suspect, then dead, and remove it after 3× the node timeout; its sessions are recovered from the owner's file or the newest replica |
| Whole cluster restarts | Each node reloads its session and replica files; the newest copy wins by `(lastSeen, nodeId)` |
| Network partition | Each side keeps serving; routing to unreachable peers fails silently |
| Slow consumer | Outbound channel fills, messages are dropped and counted |
| Protocol violation | Connection closed with the appropriate MQTT 5 reason code |

Durability is opt-in per data set: `Storage:Retained:Mode=File` journals retained
messages, `Storage:Session:Mode=File` snapshots persistent sessions, and
`Storage:Replicas:Mode=File` persists session replicas. Outbound `inflight`
acknowledgements are not persisted; inbound QoS 2 publishes waiting for PUBREL
are part of the session snapshot, so the handshake can complete. Retained
messages are a last-known-value store, not a durable message log.

## Scaling

- Vertical scaling is the simplest path; the hot path is allocation-conscious
  and lock granularity is per session and per index.
- A node owns its sessions, but clients can reconnect to any node: the session
  is claimed from its owner or recovered from a replica.
- Cluster routing cost grows with the number of advertised filters and nodes.
  The default cap is 10,000 filters per heartbeat.
- Shared subscription groups are coordinated across nodes, so a message is
  delivered once per group rather than once per node.

## What NextMQTT deliberately does not do

- No third-party broker runtime compatibility layer or private plugin API.
- No external database requirement; durability is file-based per node.
- No claim of compatibility certification with any other broker.

Those capabilities may arrive later as independent modules; see the
[roadmap](/roadmap).
