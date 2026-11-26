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
  owner when it is reachable, and otherwise falls back to the newest replica —
  but only when the replica's owner is no longer alive, so a stale copy cannot
  resurrect a session that is still running somewhere.

Sessions follow the client: a reconnect with `CleanStart=false` on another node
claims the session from its previous owner, so the offline queue and
subscriptions survive a node change. A reconnect with `CleanStart=true` still
starts from scratch.

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
  handshakes are not transferred; the client retries with `DUP`.
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

- Session handover covers subscriptions and the offline queue, not the inbound
  QoS 2 handshake or the `inflight` acknowledgement state; those messages are
  replayed from the queue and the client retries.
- A replica is at most `SessionReplicaSeconds` old and lives in memory on the
  peers, so a replica itself is lost if every other node restarts at once.
- No cross-node session persistence beyond handover and replication: if the
  owner and all replica holders die together, the session is lost.
- Filter advertisements are full snapshots, capped at 10,000 filters per
  heartbeat by default.
- Cluster routing is at-least-once per link with a deduplication cache, not a
  transactional guarantee.
- Retained sync happens once on join; a node that is partitioned from the mesh
  keeps serving its own stale retained values until it reconnects.
