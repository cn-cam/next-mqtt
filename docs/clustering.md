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
- **Session affinity.** Sessions live on the node that accepted the connection.
  Reconnecting to a different node creates a new session (`session present = 0`).
- **Shared subscriptions are node-local.** Each node evaluates its own
  `$share` groups. A publish may be delivered to one member per node. This is a
  deliberate trade-off to keep the transport stateless; global coordination is
  on the roadmap.
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
| Retained message missing on a peer | Peer was down during the retained publish; republish it |
| Remote subscribers miss messages | Filter has not propagated yet; wait one heartbeat |
| Duplicate remote deliveries | Message-id cache pruned too early; check node clocks and load |

## Limitations

- No cross-node session migration or persistence.
- No global shared-subscription coordination.
- No cross-node retained tombstones beyond best-effort replication.
- Filter advertisements are full snapshots, capped at 10,000 filters per
  heartbeat by default.
- Cluster routing is at-least-once per link with a deduplication cache, not a
  transactional guarantee.
