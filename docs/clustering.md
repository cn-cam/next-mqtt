# Clustering

NextMQTT clusters are coordinator-free meshes. Each node owns its own sessions
and routes publishes to peers that advertise a matching subscription.

## Requirements

- A shared HMAC secret of at least 16 characters.
- A reachable `AdvertiseUrl` per node (peers call it over HTTP or HTTPS).
- TCP port 8080 open between nodes.
- Synchronised clocks within five minutes (the signed request timestamp has a
  five-minute window).
- Unique `NodeId` values.

## Configuration

```json
{
  "Cluster": {
    "Enabled": true,
    "NodeId": "node-a",
    "AdvertiseUrl": "http://node-a:8080",
    "Seeds": ["http://node-b:8080", "http://node-c:8080"],
    "Secret": "shared-secret-at-least-16-chars",
    "HeartbeatSeconds": 5,
    "NodeTimeoutSeconds": 20,
    "RequestTimeoutSeconds": 5,
    "MaxFiltersPerHeartbeat": 10000
  }
}
```

Environment form:

```bash
NEXTMQTT_CLUSTER__ENABLED=true
NEXTMQTT_CLUSTER__NODEID=node-a
NEXTMQTT_CLUSTER__ADVERTISEURL=http://node-a:8080
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
NEXTMQTT_CLUSTER__SEEDS__1=http://node-c:8080
NEXTMQTT_CLUSTER__SECRET=shared-secret-at-least-16-chars
```

A node only needs one reachable seed. Membership is gossiped, so new nodes
become visible to the rest of the mesh automatically.

## Membership

- Every node starts a heartbeat loop immediately.
- A heartbeat contains the node id, advertise URL and its normal subscription
  filters.
- The receiver merges the sender into its member table and replies with its own
  member table.
- Received members are merged as well, which propagates membership through the
  mesh.
- Nodes older than `NodeTimeoutSeconds` become `suspect`, then `dead`, and are
  removed after 3× the timeout.

The dashboard **Cluster** page and `GET /api/v1/cluster` expose the current
view.

## Self-healing

- **Dead nodes are removed.** A peer silent for `NodeTimeoutSeconds` is shown
  as `suspect`, then `dead`, and is dropped from the table after three
  timeouts. Its advertised filters stop receiving forwards immediately, so a
  crash cannot keep attracting traffic.
- **Limits rescale at once.** Adding or losing a node recomputes the per-node
  share of connection, publish and topic quotas immediately, rather than waiting
  for the next sweep. The cluster total stays at the configured value.
- **Joining at runtime is normal.** A restarted or new node only needs one
  reachable seed. It merges the membership table from the first heartbeat
  response, and the other nodes learn about it the same way.
- **Retained state catches up.** On first join a node pulls the retained set
  from one peer over the signed internal API, keeping the original timestamps so
  last-write-wins still applies. Messages published while the node was away are
  therefore not lost.
- **Session replicas survive a crash.** Every `SessionReplicaSeconds` a node
  pushes snapshots of its persistent sessions to its peers. If the owner dies
  and a client reconnects elsewhere, the new node claims the session from the
  owner when it is reachable, and otherwise falls back to the newest replica.
  A replica is used only when its owner is not alive, cannot be reached during
  the claim, or answers that it does not hold the session (for example after
  losing its own file). A stale copy therefore cannot resurrect a session that
  is still running somewhere.

Sessions follow the client: a reconnect with `CleanStart=false` on another node
claims the session from its previous owner, so the offline queue and
subscriptions survive a node change. A reconnect with `CleanStart=true` still
starts from scratch.

## Availability model

The design assumes a client is connected to at most one node at a time and that
clients reconnect on their own. Within that assumption:

| Failure | Effect | What the cluster does |
| --- | --- | --- |
| Client TCP drop | Session goes offline | Queue keeps accepting messages until the expiry interval |
| Node crash | Its sessions become unreachable; routing stops sending to it | Peers mark it dead within `NodeTimeoutSeconds`; messages for its subscribers are lost until the client reconnects and claims the session elsewhere |
| Client reconnects elsewhere | Session is on the dead node | The new node claims it, or falls back to the newest replica if the owner is gone, cannot be reached, or reports no session |
| Owner disk lost, node restarted | Session store empty | Replicas on peers (file-backed if configured) restore it; a peer that answered "no session" does not block the fallback |
| All nodes restarted | Memory is empty | Each node reloads its session file and its replica file; the newest copy wins by `(lastSeen, nodeId)` |
| Partition | Two views of membership | Only the side that owns the live client serves it; a replica is never used while its recorded owner is alive in the local view |
| Slow replica push | Replica up to one batch round old (`SessionReplicaSeconds`, longer when the session count exceeds `SessionReplicaBatchSize`) | A recovered session may miss recent queued messages |

**Recovery point.** On the replica side a session is normally at most
`SessionReplicaSeconds` (default 5) old. Each round pushes
`SessionReplicaBatchSize` (default 200) sessions, so a node with more persistent
sessions than that has a full sweep of
`ceil(sessions / batch size) × SessionReplicaSeconds`; the oldest replica can be
that old. The owner's own file is at most `SessionSnapshotSeconds` (30) old.
Retained messages are journaled per write. Messages already delivered but not yet
acknowledged are replayed from the queue; a duplicate is possible, which MQTT
tolerates at QoS 1/2.

**Recovery time.** A reconnect plus one internal round trip: the new node asks
live peers, then reads its own replica file. No operator action is needed.

**Deliberate limits.** The outbound `inflight` acknowledgement state is not
transferred: those messages are replayed from the queue and may arrive twice,
which MQTT tolerates at QoS 1/2. Inbound QoS 2 publishes that are still waiting
for PUBREL are transferred with the session, so the client can finish the
handshake without resending the payload. Sessions with a zero expiry are not
replicated, because they cannot outlive the connection by definition. There is
no cross-node ordering guarantee and no exactly-once delivery.

**Split-brain stance.** Session ownership is decided by evidence, not by a
consensus round: a live owner always wins; if it answers without the session, the
newest replica is used; ties between two restored copies are broken by
`(lastSeen, nodeId)`, so every node reaches the same verdict without
coordination. The trade-off is a short window where a partitioned owner and a
recovered copy could both accept the same client id — the client itself can only
be in one place, so this is acceptable for MQTT semantics.

## Routing

1. A publish is delivered locally.
2. If clustering is enabled, the router selects live peers:
   - retained publishes go to every live peer, so retained state converges;
   - other publishes go only to peers advertising a matching filter.
3. The peer verifies the HMAC signature, deduplicates by message id and calls
   the local routing path without re-forwarding.

```text
publisher ──► node-a ──► local subscribers
                  │
                  ├── signed POST /internal/cluster/publish ──► node-b
                  └── signed POST /internal/cluster/publish ──► node-c
```

A slow or unreachable peer does not block local delivery: each forward has its
own request timeout and failures are logged at debug level.

## Retained messages

Retained publishes are replicated to every live node. A new subscriber on any
node receives the retained value after its SUBACK. Deleting a retained message
(empty payload) is also replicated.

## Delivery semantics

- **At-most-once per path.** Cluster forwarding is best-effort HTTP. If a peer
  is down, messages routed to it are lost; MQTT QoS does not span nodes.
- **Duplicate suppression.** A bounded seen-message cache rejects repeat
  delivery of the same cluster message.
- **Session affinity with handover.** A session normally lives on the node that
  accepted the connection. When a client reconnects with `CleanStart=false` to
  another node, that node claims the session from its previous owner, restores
  its subscriptions and offline queue, and reports `session present = 1`. The
  old connection is closed with 0x8E and its will is not published. Inbound QoS 2
  publishes that are waiting for PUBREL travel with the session, so the client
  can send PUBREL on the new node and the message is routed there.
- **Shared subscriptions span the cluster.** Nodes advertise their `$share`
  groups in heartbeats; each message is assigned to exactly one node that hosts
  the group, chosen by hashing the message id and group name, so a group member
  on another node does not receive a duplicate copy.
- **Filter propagation is eventual.** New filters are advertised on the next
  heartbeat. A publish immediately after subscribing may not reach a remote
  subscriber until the next heartbeat (default 5 s).

## Security

The internal API uses HMAC-SHA256 over `timestamp.body` with a five-minute
clock-skew window. The timestamp and signature are sent in
`X-NextMQTT-Timestamp` and `X-NextMQTT-Signature`.

- The secret is never sent over the wire.
- Use HTTPS for `AdvertiseUrl` if node traffic crosses untrusted networks.
- Restrict `/internal/cluster/*` at the ingress or with NetworkPolicy.
- Rotate the secret by restarting nodes with the new value; rolling rotation is
  not supported yet because there is a single secret.

Dashboard sign-in is separate from the cluster secret. Each node signs bearer
tokens with its own key unless `NEXTMQTT_ADMIN_TOKEN_SECRET` is set, so a token
issued by one node is rejected by another. Set the same value on every node when
operators should sign in once and use any node's dashboard or API.

## Kubernetes specifics

The StatefulSet uses a headless service and pod DNS:

```text
NEXTMQTT_CLUSTER__NODEID=$(POD_NAME)
NEXTMQTT_CLUSTER__ADVERTISEURL=http://$(POD_NAME).nextmqtt-headless.nextmqtt.svc.cluster.local:8080
```

Seeds reference the first three pod names. If you scale beyond the initial
replica count, new pods contact the seeds and are discovered through gossip.

## Verifying a cluster

```bash
# membership
curl -s http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN" | jq '.nodes'

# cross-node delivery
dotnet run --project src/NextMqtt.Cli -- sub --port 1884 --topic cluster/# --count 1 &
dotnet run --project src/NextMqtt.Cli -- pub --port 1883 --topic cluster/test --message hello --qos 1
```

Wait at least one heartbeat after subscribing before publishing.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Only one node in the list | `AdvertiseUrl` reachability from peers; seeds typo; firewall on 8080 |
| `401` in peer logs | Secrets differ, or clocks differ by more than 5 minutes |
| Node alternates alive/dead | Heartbeat interval vs. timeout; CPU starvation |
| Retained message missing on a peer | The peer joined before the sync existed; restart it or republish |
| Remote subscribers miss messages | Filter has not propagated yet; wait one heartbeat |
| Duplicate remote deliveries | Message-id cache pruned too early; check node clocks and load |
| Limits look too low after a restart | The node still counts a dead peer until 3× `NodeTimeoutSeconds` |

## Limitations

- Session handover covers subscriptions, the offline queue and inbound QoS 2
  publishes still awaiting PUBREL. The outbound `inflight` acknowledgement state
  is not transferred; those messages are replayed from the queue and the client
  may see a duplicate.
- A replica is normally at most `SessionReplicaSeconds` old, or one full sweep
  when the node has more sessions than `SessionReplicaBatchSize`. Without the
  file store it lives only in memory, so a restart of every node loses the
  replicas; set `Storage:Replicas:Mode` to `File` to keep them across restarts.
- No cross-node session persistence beyond handover and replication: if the
  owner and every replica holder and their disks are lost together, the session
  is gone.
- Each node keeps at most `Cluster:SessionReplicaCapacity` replicas (default
  20,000); the oldest are evicted when the table is full.
- Filter advertisements are full snapshots, capped at 10,000 filters per
  heartbeat by default.
- Cluster routing is at-least-once per link with a deduplication cache, not a
  transactional guarantee.
- Retained sync happens once on join; a node that is partitioned from the mesh
  keeps serving its own stale retained values until it reconnects.
