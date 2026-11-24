# Operations

This page covers day-two concerns: health, metrics, logs, capacity, upgrades and
troubleshooting.

## Health endpoints

| Endpoint | Purpose |
| --- | --- |
| `GET /healthz` | Process is alive; suitable for liveness probes |
| `GET /readyz` | Broker is ready to accept traffic |
| `GET /api/v1/health` | API health with version |
| `GET /api/v1/ready` | API readiness with session count |

Both are unauthenticated by design. Do not put secrets in health responses.

## Metrics

`GET /metrics` exposes Prometheus text format. Authentication is disabled by
default; set `Admin:MetricsRequireAuth=true` to require a bearer token or API
key.

| Metric | Type | Meaning |
| --- | --- | --- |
| `nextmqtt_connections_opened_total` | counter | Accepted CONNECTs |
| `nextmqtt_connections_closed_total` | counter | Closed connections |
| `nextmqtt_connections_rejected_total` | counter | Rejected during CONNECT |
| `nextmqtt_authentication_failures_total` | counter | Failed auth attempts |
| `nextmqtt_publishes_received_total` | counter | PUBLISH packets accepted |
| `nextmqtt_publishes_sent_total` | counter | PUBLISH packets delivered |
| `nextmqtt_bytes_received_total` | counter | Bytes read from MQTT sockets |
| `nextmqtt_bytes_sent_total` | counter | Bytes written to MQTT sockets |
| `nextmqtt_messages_dropped_total` | counter | Messages dropped by policy or back-pressure |
| `nextmqtt_retransmissions_total` | counter | QoS retransmissions |
| `nextmqtt_subscriptions_created_total` | counter | Successful subscriptions |
| `nextmqtt_sessions_expired_total` | counter | Sessions removed after expiry |
| `nextmqtt_connections_rate_limited_total` | counter | CONNECTs rejected by the connection rate limit |
| `nextmqtt_publishes_rate_limited_total` | counter | PUBLISH packets dropped by the per-client rate limit |
| `nextmqtt_clients_connected` | gauge | Online connections |
| `nextmqtt_sessions` | gauge | Sessions in the registry |
| `nextmqtt_subscriptions` | gauge | Active subscriptions |
| `nextmqtt_retained_messages` | gauge | Retained messages |
| `nextmqtt_cluster_nodes` | gauge | Known cluster nodes |
| `nextmqtt_build_info` | gauge | Version label |

Example Prometheus scrape configuration:

```yaml
scrape_configs:
  - job_name: nextmqtt
    metrics_path: /metrics
    static_configs:
      - targets: ["nextmqtt-http:8080"]
```

The Kubernetes manifests add `prometheus.io/scrape` annotations.

## Dashboard

The overview page polls the API every three seconds. It shows:

- connected clients and total sessions,
- subscriptions and retained messages,
- publish throughput for the last minute,
- connection, authentication, drop and retransmission counters,
- cluster membership.

Clients, subscriptions and retained tables poll every five seconds and paginate
server-side. The publish console uses the same ACL-free administrative path as
the REST API.

The MQTT debugger (Messaging → Debugger) connects straight from the browser to
the broker over WebSocket, so it exercises the real protocol rather than the
administrative API. It supports MQTT 3.1.1 and 5.0, QoS 0–2, retained messages,
will messages, MQTT 5 session and publish properties, topic filters with noLocal,
retain-as-published and retain handling, PING, a packet trace and JSON log
export. It uses the same credentials as any MQTT client and is subject to the
broker's authentication and ACL rules.

## Logs

Logs are written to stdout. Useful messages include:

- listener startup with transport and address,
- client connect/disconnect with client id and session-present flag,
- cluster join/leave/timeout events,
- generated bootstrap password and ephemeral token secret warnings,
- protocol errors at debug level.

Set `Logging:LogLevel:NextMqtt=Debug` for packet-level diagnostics. Avoid debug
logging in production when payload privacy matters; the broker does not log
payloads.

## Capacity planning

Start with:

| Workload | CPU | Memory |
| --- | --- | --- |
| Up to 10k idle clients | 0.5 core | 256–512 MiB |
| 10k clients with moderate QoS 1 traffic | 1–2 cores | 512 MiB–1 GiB |
| Large retained sets / offline queues | 1+ core | size for payloads and queues |

Memory is dominated by:

- session objects and subscription entries,
- offline queues (`MaxQueuedMessages` per persistent session),
- retained message payloads,
- in-flight windows.

Lower the queue limit or session expiry when memory is constrained. There is no
global retained-message quota in this release; monitor
`nextmqtt_retained_messages` and payload sizes.

### Measured reference numbers

| Scenario | Result |
| --- | --- |
| 1,000 concurrent connections | 1,000 accepted in 1.09 s (≈920 conn/s) |
| 5,000 concurrent connections | 5,000 accepted in 3.16 s (≈1,580 conn/s), 0 failures |
| QoS 0, 50 clients × 2,000 messages, 64 B | ≈194,000 msg/s |
| QoS 0, 100 clients × 2,000 messages, 128 B | ≈186,000 msg/s |
| QoS 1, 20 clients × 500 messages | ≈19,800 msg/s |

Hardware: one machine, Windows 11, Intel Core i7-11700F (8 cores / 16 threads),
32 GiB RAM, broker and load generator sharing the same CPU over loopback. These
numbers are deliberately conservative — a separate generator, Linux and a
release build on a dedicated host do better. Reproduce them with
`nextmqtt bench` and `nextmqtt bench-connections`; TLS, retained-heavy topics
and large fan-out all reduce them.

When the broker runs under Docker Desktop with published ports, the host's port
forwarder saturates at roughly 2,000 concurrent connections on this machine —
above that, connects fail before they reach the broker. Run the generator inside
the container network, or on a Linux host, to measure connection capacity.

## Session lifecycle

- Persistent sessions remain in memory until their expiry deadline.
- The maintenance loop checks expiry every `Broker:SweepInterval` (30 s by
  default).
- Disconnecting a client with `cleanStart=true` discards the prior session at
  the next CONNECT.
- Dashboard "Disconnect" sends MQTT 5 reason `0x98` and closes the socket. The
  session still follows normal expiry rules.

## Upgrades

1. Drain or scale down one node at a time.
2. Because sessions are in-memory, plan for clients to reconnect and re-subscribe.
3. Replace the image tag in Compose/Helm.
4. Watch `/readyz` and `nextmqtt_clients_connected` during rollout.
5. For clusters, keep at least two nodes alive to preserve routing coverage.

A rolling restart loses queued messages and retained state. Use retained
messages as recoverable configuration, not as a durable event log, until
persistence lands.

## Troubleshooting

| Symptom | Likely cause | Action |
| --- | --- | --- |
| Dashboard returns 401 | Expired token or restart with ephemeral secret | Sign in again; set `NEXTMQTT_ADMIN_TOKEN_SECRET` |
| All MQTT clients rejected | Anonymous disabled and no users/JWT | Configure users or enable anonymous |
| SUBACK `0x87` | ACL denies the filter | Review `Acl:Rules` and default policy |
| PUBACK `0x87` | ACL denies the topic | Review publish rules |
| Clients disconnect after idle | Keep-alive exceeded | Check client keep-alive and network idle timeouts |
| Messages vanish for offline client | Session not persistent or queue full | Use session expiry / clean start false; raise queue limit |
| Cluster shows suspect/dead | Heartbeat blocked by firewall or URL wrong | Check `AdvertiseUrl`, port 8080, secret, clock skew |
| High drop counter | Slow consumer or small queue/channel | Inspect client throughput; increase limits carefully |
| `nextmqtt_connections_rejected_total` rises | Auth or client id failures | Check auth logs and client id rules |

## Backups

There is no on-disk state to back up unless retained persistence is enabled. With
`Storage:Retained:Mode` set to `file`, the journal at `Storage:Retained:Path`
holds every retained message and can be copied while the broker runs (it is
append-only and compaction uses an atomic rename). Sessions, queues and
subscriptions are still in memory, so they do not survive a restart.

Configuration lives in environment variables and Kubernetes/Compose files; back
those up. The admin token secret and cluster secret must be preserved to avoid
invalidating tokens or breaking node trust.
