# Configuration

NextMQTT uses ASP.NET Core's configuration system. Settings can come from
`appsettings.json`, environment variables, command-line arguments or a secret
store.

## Precedence

Later providers win:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. user secrets (`dotnet user-secrets`, Development only)
4. environment variables
5. `NEXTMQTT_*` environment variables
6. command-line arguments

The container image sets `ASPNETCORE_ENVIRONMENT=Production`. The default
`appsettings.json` is intentionally permissive for local evaluation; override
authentication and ACL settings before production use.

## Environment variable format

Two forms are supported.

**Prefixed configuration keys** use `NEXTMQTT_` and `__` for section
separators:

```bash
NEXTMQTT_BROKER__MAXPACKETSIZE=2097152
NEXTMQTT_SERVER__HTTP__PORT=9090
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
```

**Bootstrap variables** are read explicitly:

| Variable | Purpose |
| --- | --- |
| `NEXTMQTT_ADMIN_PASSWORD` | Plain admin password, hashed at startup (recommended) |
| `NEXTMQTT_ADMIN_USERNAME` | Admin username override |
| `NEXTMQTT_ADMIN_TOKEN_SECRET` | Stable HMAC secret for dashboard tokens |
| `NEXTMQTT_ALLOW_ANONYMOUS` | `true`/`false` anonymous MQTT access |

::: tip Admin tokens
If `NEXTMQTT_ADMIN_TOKEN_SECRET` is unset, an ephemeral key is generated. All
tokens are invalidated on restart, and multi-node dashboards cannot share
tokens. Set the same value on every node in production.
:::

## Server

```json
{
  "Server": {
    "Tcp": { "Enabled": true, "Host": "0.0.0.0", "Port": 1883 },
    "Tls": {
      "Enabled": false,
      "Host": "0.0.0.0",
      "Port": 8883,
      "CertificatePath": "/certs/server.pfx",
      "CertificatePassword": "secret",
      "RequireClientCertificate": false
    },
    "WebSocket": { "Enabled": true, "Path": "/mqtt" },
    "Http": {
      "Port": 8080,
      "WebSocketPort": 8083,
      "TlsPort": 8084,
      "CertificatePath": null,
      "CertificatePassword": null,
      "AllowedOrigins": ["http://localhost:5173"]
    }
  }
}
```

| Option | Default | Description |
| --- | --- | --- |
| `Tcp:Enabled` | `true` | Raw MQTT listener |
| `Tcp:Host` | `0.0.0.0` | IP address to bind |
| `Tcp:Port` | `1883` | MQTT port |
| `Tls:Enabled` | `false` | TLS MQTT listener |
| `Tls:CertificatePath` | — | PFX/PKCS#12 or PEM path |
| `Tls:CertificatePassword` | — | PFX password |
| `Tls:RequireClientCertificate` | `false` | Request a client certificate (validation is platform default) |
| `WebSocket:Enabled` | `true` | Serve MQTT over WebSocket |
| `WebSocket:Path` | `/mqtt` | HTTP path |
| `Http:Port` | `8080` | API and dashboard port |
| `Http:WebSocketPort` | `8083` | Additional Kestrel endpoint for browser MQTT |
| `Http:TlsPort` | `8084` | HTTPS endpoint when a certificate is configured |
| `Http:AllowedOrigins` | localhost dev origins | CORS allow-list for the dashboard API |
| `DrainTimeoutSeconds` | `5` | On shutdown, how long to wait for inflight QoS acknowledgements |

Setting `Http:WebSocketPort` equal to `Http:Port` disables the extra endpoint.

## Broker

```json
{
  "Broker": {
    "MaxPacketSize": 1048576,
    "MaxInflightMessages": 32,
    "MaxQueuedMessages": 1000,
    "MaxSubscriptionsPerSession": 1000,
    "MaxTopicAlias": 32,
    "ConnectTimeout": "00:00:10",
    "RetransmitInterval": "00:00:10",
    "SweepInterval": "00:00:30",
    "MaxClientIdLength": 256,
    "AllowZeroLengthClientId": true
  }
}
```

| Option | Default | Description |
| --- | --- | --- |
| `MaxPacketSize` | 1 MiB | Hard limit per packet |
| `MaxInflightMessages` | 32 | Outbound QoS 1/2 window and advertised receive maximum |
| `MaxQueuedMessages` | 1000 | Offline queue per persistent session |
| `MaxSubscriptionsPerSession` | 1000 | Subscription quota |
| `MaxTopicAlias` | 32 | Inbound topic alias table size |
| `MaxSubscriptionIdentifiersPerClient` | 0 | Subscriptions per client that may carry an identifier; 0 disables |
| `ConnectTimeout` | 10 s | Time allowed to send CONNECT |
| `RetransmitInterval` | 10 s | QoS retransmission timer |
| `SweepInterval` | 30 s | Expiry and maintenance cadence |
| `MaxClientIdLength` | 256 | Reject longer identifiers |
| `AllowZeroLengthClientId` | `true` | Assign an id when the client sends none |

## Rate limits

Off by default. Both limits are token buckets: a burst is allowed, then the
sustained rate applies.

```json
{
  "RateLimits": {
    "ConnectPerSecondPerIp": 0,
    "ConnectBurst": 0,
    "PublishPerSecondPerClient": 0,
    "PublishBurst": 0
  }
}
```

| Option | Default | Description |
| --- | --- | --- |
| `ConnectPerSecondPerIp` | `0` | Accepted CONNECTs per second per remote address; `0` disables |
| `ConnectBurst` | `0` | Burst size for connects; `0` uses the per-second rate |
| `PublishPerSecondPerClient` | `0` | Accepted PUBLISH packets per second per client; `0` disables |
| `PublishBurst` | `0` | Burst size for publishes; `0` uses the per-second rate |
| `Topics[].Filter` | – | Topic filter for a shared quota |
| `Topics[].PublishPerSecond` | `0` | Accepted publishes per second across all clients |
| `Topics[].Burst` | `0` | Burst size; `0` uses the per-second rate |

```bash
NEXTMQTT_RATELIMITS__CONNECTPERSECONDPERIP=20 \
NEXTMQTT_RATELIMITS__PUBLISHPERSECONDPERCLIENT=500 \
nextmqtt-server
```

Topic quotas are configured as a list, so a deployment can protect a few hot
topics without throttling everything:

```json
{
  "RateLimits": {
    "Topics": [
      { "Filter": "telemetry/#", "PublishPerSecond": 2000, "Burst": 4000 },
      { "Filter": "commands/#", "PublishPerSecond": 50 }
    ]
  }
}
```

Behaviour when a limit is hit:

- **CONNECT** above the per-address rate is answered with CONNACK
  `0x9F Connection rate exceeded` on MQTT 5, or `0x03 Server unavailable` on
  MQTT 3.1.1, then the socket is closed.
- **PUBLISH** above the per-client rate is *dropped* but still acknowledged
  (PUBACK, or PUBREC with `0x96 Message rate too high` on MQTT 5), so clients do
  not retransmit. Dropped messages increment
  `nextmqtt_publishes_rate_limited_total`, `nextmqtt_messages_dropped_total` and
  `nextmqtt_publishes_received_total`.
- CONNECT rejections increment `nextmqtt_connections_rate_limited_total`.

Limits describe the **whole cluster**. Each node divides the configured rate and
burst by the number of live nodes (minimum one token), so three nodes with
`PublishPerSecondPerClient = 500` allow about 500 messages per second per
client in total, not 1500. Membership is re-evaluated every sweep; a node that
just joined or left can transiently overshoot because there is no global
counter. A standalone broker uses the configured values unchanged.

## Storage

Retained messages live in memory by default. Set the store to `File` to journal
them to disk so they survive a restart; sessions, queues and subscriptions stay
in memory in this release.

```json
{
  "Storage": {
    "Retained": {
      "Mode": "Memory",
      "Path": "data/retained.log",
      "CompactThresholdBytes": 4194304
    }
  }
}
```

| Option | Default | Description |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` or `File` |
| `Path` | `data/retained.log` | Journal file; missing parent directories are created |
| `CompactThresholdBytes` | 4 MiB | Rewrite the journal once it grows past this size |

```bash
NEXTMQTT_STORAGE__RETAINED__MODE=file \
NEXTMQTT_STORAGE__RETAINED__PATH=/app/data/retained.log \
nextmqtt-server
```

The journal is JSON lines: one `set` or `remove` operation per line with the
topic, base64 payload, QoS, timestamps and MQTT 5 properties. The broker replays
it at startup, skips expired entries and compacts when the file is too large; a
torn final line is ignored instead of failing the boot. Each cluster node keeps
its own journal, so a node that was offline still recovers what it had stored.

Persistent sessions are snapshotted separately:

```json
{
  "Storage": {
    "Session": {
      "Mode": "Memory",
      "Path": "data/sessions.json",
      "SnapshotSeconds": 30
    }
  }
}
```

| Option | Default | Description |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` or `File` |
| `Path` | `data/sessions.json` | Snapshot file, replaced atomically |
| `SnapshotSeconds` | 30 | Rewrite interval; unchanged state is skipped |

A snapshot holds every persistent session: identity, expiry, subscriptions and
the offline queue with its MQTT 5 properties. Inflight QoS handshakes are not
persisted — after a restart those messages are delivered again on the next
publish rather than resumed, and the window is bounded by
`MaxInflightMessages`. Snapshots are also written on graceful shutdown.

## Backups

`GET /api/v1/backup` returns a portable JSON document with retained messages and
persistent sessions; `POST /api/v1/backup` imports one. Import replaces the
retained set, adds sessions whose client id is not currently online and keeps
live sessions untouched.

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'content-type: application/json' \
  -d '{"username":"admin","password":"..."}' | jq -r .token)

curl -s http://localhost:8080/api/v1/backup \
  -H "Authorization: Bearer $TOKEN" > nextmqtt-backup.json

curl -s -X POST http://localhost:8080/api/v1/backup \
  -H "Authorization: Bearer $TOKEN" \
  -H 'content-type: application/json' \
  --data @nextmqtt-backup.json
```


## Integrations

Outbound webhooks feed matching publishes to an HTTP endpoint without blocking
MQTT delivery. Each rule has its own bounded queue and background worker; when a
queue is full the event is dropped and counted.

```json
{
  "Integrations": {
    "Webhooks": [
      {
        "Name": "audit",
        "Filter": "audit/#",
        "Url": "https://ops.example.com/hooks/mqtt",
        "TimeoutMs": 3000,
        "MaxQueue": 1000,
        "Headers": { "Authorization": "Bearer <token>" },
        "IncludePayload": true
      }
    ]
  }
}
```

| Option | Default | Description |
| --- | --- | --- |
| `Filter` | `#` | Topic filter that selects messages |
| `Url` | – | Absolute http/https endpoint |
| `TimeoutMs` | 5000 | Per-attempt timeout |
| `MaxQueue` | 1000 | Buffered events before drops |
| `Headers` | – | Extra request headers |
| `IncludePayload` | `true` | Send the base64 payload |

Delivery is at-least-once per event per rule, with up to three attempts and
backoff; failures are logged and counted. The body is JSON:
`{ name, topic, qos, retain, receivedAt, payload }`. Only the node that first
accepted the publish posts the event, so a cluster does not duplicate webhooks.
### Routing rules

```json
{
  "Integrations": {
    "Rules": [
      { "Filter": "debug/#", "Action": "drop" },
      { "Filter": "health/ping", "Action": "drop" }
    ]
  }
}
```

A matching message is discarded before retained storage, delivery and cluster
forwarding, and counts in `nextmqtt_messages_dropped_total`. `drop` is the only
action in this release; rewrite and enrichment are on the roadmap.
## Authentication

```json
{
  "Authentication": {
    "AllowAnonymous": false,
    "Superusers": ["ops"],
    "Users": [
      {
        "Username": "device-1",
        "PasswordHash": "pbkdf2-sha256$210000$...$...",
        "Roles": ["devices"]
      }
    ],
    "Jwt": {
      "Secret": "at-least-32-random-characters",
      "Issuer": null,
      "Audience": null,
      "UsernameClaim": "sub",
      "RoleClaim": "roles",
      "RequireExpiration": true
    }
  }
}
```

Generate a password hash with the CLI:

```bash
dotnet run --project src/NextMqtt.Cli -- hash-password --password 'correct horse battery staple'
```

The hash format is `pbkdf2-sha256$iterations$salt-base64$hash-base64`. The
default is 210,000 iterations.

JWT authentication uses the MQTT password field. A token is accepted when it
has a valid HS256 signature, `exp` (by default), and the configured issuer and
audience. The `sub` claim becomes the username; the `roles` claim determines
superuser status (`admin`) and can be used by future policy modules.

## ACL

```json
{
  "Acl": {
    "DefaultPolicy": "Deny",
    "Rules": [
      { "Effect": "Allow", "Action": "Subscribe", "Topic": "$SYS/#" },
      { "Effect": "Allow", "Action": "Both", "Topic": "devices/${clientid}/#" },
      { "Effect": "Allow", "Action": "Subscribe", "Topic": "public/#" },
      { "Effect": "Deny", "Action": "Publish", "Topic": "public/audit/#", "Username": "*" }
    ]
  }
}
```

Rules are evaluated in order; the first matching rule decides. A rule matches
when its `Action`, `Username` and `ClientId` globs match. Supported
placeholders in `Topic` are `${username}` and `${clientid}`. `DefaultPolicy` is
`Deny` or `Allow`.

Subscribe rules must **cover** the requested filter, not merely overlap it. For
example, `sensors/#` covers `sensors/+/temperature`; `sensors/+` does not cover
`sensors/#`.

Superusers bypass all ACL checks. A user is a superuser when listed in
`Authentication:Superusers` or assigned the `admin` role.

## Admin

```json
{
  "Admin": {
    "Username": "admin",
    "TokenSecret": null,
    "TokenLifetimeMinutes": 480,
    "MetricsRequireAuth": false,
    "ApiKeySha256": []
  }
}
```

| Option | Description |
| --- | --- |
| `Username` | Dashboard username |
| `PasswordHash` | PBKDF2 hash; `NEXTMQTT_ADMIN_PASSWORD` is easier |
| `TokenSecret` | HMAC key for dashboard tokens |
| `TokenLifetimeMinutes` | Token lifetime, clamped to 5–10,080 minutes |
| `MetricsRequireAuth` | Protect `/metrics` with the admin token |
| `ApiKeySha256` | SHA-256 hex digests accepted via `X-API-Key` |

Generate an API key hash:

```bash
printf '%s' 'my-api-key' | sha256sum
```

## Cluster

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

When clustering is enabled, `AdvertiseUrl` must be reachable from peers and
`Secret` must be at least 16 characters. See [Clustering](/clustering).

## Logging

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "NextMqtt": "Information"
    }
  }
}
```

Logs go to stdout in a plain, container-friendly format. Set
`Logging:LogLevel:NextMqtt=Debug` for protocol-level detail.

## Reverse proxies

Kestrel does not terminate TLS for the dashboard unless `Http:CertificatePath`
is set. If you terminate at an ingress or load balancer:

- forward `X-Forwarded-For` and `X-Forwarded-Proto`;
- enable WebSocket upgrades for `/mqtt`;
- keep read timeouts above the MQTT keep-alive interval;
- restrict `/internal/cluster/*` to broker-to-broker traffic.
