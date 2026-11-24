# ADR 0002: Signed HTTP gossip instead of a consensus store

- Status: accepted
- Date: 2026-11-24

## Context

Clustering must work without an external coordinator, remain understandable and
avoid introducing a consensus dependency into the broker. Sessions are owned by
the node that accepted the connection, so full state replication is unnecessary
for a first release.

## Decision

Nodes exchange membership and normal subscription filters over HTTP:

- heartbeats every few seconds, signed with HMAC-SHA256;
- responses carry the responder's member table (gossip);
- publishes are forwarded only to nodes with a matching advertised filter;
- retained publishes are replicated to every live node;
- message ids deduplicate remote delivery;
- remote publishes are not re-forwarded.

The cluster secret is mandatory when clustering is enabled.

## Consequences

- Adding nodes requires no coordinator or database.
- Filter propagation is eventually consistent (one heartbeat by default).
- Cross-node delivery is best-effort; a peer outage can lose routed messages.
- Shared subscriptions are node-local.
- The filter set is sent in full per heartbeat, capped by configuration.
