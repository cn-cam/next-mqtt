# REST API

The management API is served on `Server:Http:Port` (default 8080) under
`/api/v1`. The dashboard uses the same endpoints. Its **System → API console**
page can send every endpoint below with the signed-in session.

## Authentication

Public endpoints:

- `GET /healthz`, `GET /readyz`
- `GET /api/v1/health`, `GET /api/v1/ready`, `GET /api/v1/version`
- `POST /api/v1/auth/login`
- `GET /metrics` when `Admin:MetricsRequireAuth=false`

Everything else requires one of:

```http
Authorization: Bearer <token>
```

```http
X-API-Key: <api-key>
```

Tokens are issued by login and expire according to
`Admin:TokenLifetimeMinutes`. API keys are matched by SHA-256 digest.

### Login

```bash
TOKEN=$(curl -s http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"change-me-now"}' | jq -r .token)
```

```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAt": "2026-11-24T13:00:00Z",
  "username": "admin"
}
```

### Errors

Errors use a stable shape:

```json
{ "code": "unauthorized", "message": "A valid bearer token or API key is required." }
```

| Status | Meaning |
| --- | --- |
| 400 | Invalid request body or topic |
| 401 | Missing/invalid token or API key |
| 404 | Resource not found |
| 409 | Cluster message already seen |
| 413 | Request body too large |
| 500 | Unhandled server error |

## Endpoints

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/v1/health` | Version and liveness |
| GET | `/api/v1/ready` | Readiness and session count |
| GET | `/api/v1/version` | Product, runtime and environment |
| POST | `/api/v1/auth/login` | Issue a dashboard token |
| GET | `/api/v1/auth/me` | Current principal |
| GET | `/api/v1/stats` | Counters and 60-second publish series |
| GET | `/api/v1/clients` | Paginated clients/sessions |
| GET | `/api/v1/clients/{clientId}` | One client |
| DELETE | `/api/v1/clients/{clientId}` | Disconnect a client |
| GET | `/api/v1/subscriptions` | Paginated subscriptions |
| GET | `/api/v1/retained` | Paginated retained messages |
| DELETE | `/api/v1/retained?topic=...` | Delete a retained message |
| POST | `/api/v1/publish` | Publish through the broker |
| GET | `/api/v1/cluster` | Membership snapshot |
| GET | `/api/v1/logs` | Recent log entries, filtered by level and text |
| GET | `/api/v1/plugins` | Loaded plugins and which hooks are active |
| POST | `/api/v1/plugins/reload` | Rescan the plugin directory and swap the hooks |
| GET | `/api/v1/config` | Effective safe configuration |
| GET | `/metrics` | Prometheus exposition |

### Logs

```http
GET /api/v1/logs?level=Warning&search=cluster&limit=200
```

```json
{
  "entries": [
    {
      "timestamp": "2026-11-25T20:00:52.123+00:00",
      "level": "Warning",
      "category": "NextMqtt.Broker.Clustering.ClusterRouter",
      "message": "Cluster node node-b timed out and was removed",
      "exception": null
    }
  ],
  "total": 42,
  "capacity": 2000,
  "level": "Warning"
}
```

`level` accepts `trace`, `debug`, `information`/`info`, `warning`/`warn`,
`error`, `critical`/`fatal`. Entries are kept in a bounded in-memory ring, so
`total` can exceed `capacity`.

### Plugins

```http
GET /api/v1/plugins
```

```json
{
  "enabled": true,
  "directory": "plugins",
  "contractVersion": 1,
  "authenticator": "ProbeAuth.DenyAllAuthenticator",
  "authorizer": null,
  "authenticatorLoaded": true,
  "authorizerLoaded": false,
  "instances": 1,
  "reloads": 0,
  "plugins": [
    { "name": "probe-auth", "version": "1.0.0", "directory": "/app/plugins/probe-auth", "assembly": "ProbeAuth" }
  ]
}
```

`POST /api/v1/plugins/reload` re-reads the `Plugins` section from configuration,
rescans the directory, loads a new host, swaps the authenticator and authorizer,
and then disposes and unloads the previous host. A plugin that disappears from
disk falls back to the built-in implementation on the next reload; in-flight
requests finish against the old instance.

Plugins that implement `IPluginLifecycle` receive `Initialize(PluginContext)`
after construction and `Dispose()` before their load context is unloaded, so
files, threads and sockets can be released deterministically. A plugin whose
`Initialize` throws is skipped and logged; the rest keep loading.

### List clients

```http
GET /api/v1/clients?search=device&page=1&pageSize=50
```

```json
{
  "items": [
    {
      "clientId": "device-1",
      "username": "device-1",
      "protocolVersion": "5.0",
      "connected": true,
      "remoteAddress": "10.0.0.12:52133",
      "connectedAt": "2026-11-24T12:00:00Z",
      "lastSeen": "2026-11-24T12:00:30Z",
      "sessionExpiryInterval": 3600,
      "subscriptions": 4,
      "inflightMessages": 0,
      "queuedMessages": 0,
      "isSuperuser": false
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

Search matches client id, username and remote address. Page size is clamped to
1–200.

### Disconnect a client

```bash
curl -X DELETE http://localhost:8080/api/v1/clients/device-1 \
  -H "Authorization: Bearer $TOKEN"
```

Returns `204` when the client was found, `404` otherwise.

### Subscriptions

```http
GET /api/v1/subscriptions?search=sensors&page=1&pageSize=100
```

Each item contains `clientId`, `filter`, `sharedGroup`, `qos`, `noLocal`,
`retainAsPublished`, `retainHandling` and `identifier`.

### Retained messages

```http
GET /api/v1/retained?search=sensors&page=1&pageSize=100
```

```json
{
  "items": [
    { "topic": "sensors/demo", "payloadBytes": 4, "qoS": "AtLeastOnce", "storedAt": "..." }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 100
}
```

Delete by query parameter (topic names contain `/`):

```bash
curl -X DELETE "http://localhost:8080/api/v1/retained?topic=sensors%2Fdemo" \
  -H "Authorization: Bearer $TOKEN"
```

### Publish

```bash
curl -X POST http://localhost:8080/api/v1/publish \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{
    "topic": "devices/demo/command",
    "payload": "{\"action\":\"reboot\"}",
    "encoding": "plain",
    "qos": 1,
    "retain": false
  }'
```

```json
{ "topic": "devices/demo/command", "bytes": 18, "qos": 1, "retain": false }
```

- `encoding` is `plain` (UTF-8) or `base64`.
- `qos` must be 0, 1 or 2.
- `retain` stores the message as the last-known value.
- This is an administrative publish; client ACL rules do not apply.

### Cluster

```json
{
  "nodeId": "node-a",
  "status": "clustered",
  "nodes": [
    {
      "nodeId": "node-a",
      "address": "http://node-a:8080",
      "status": "alive",
      "lastSeen": "2026-11-24T12:00:00Z",
      "isSelf": true
    }
  ]
}
```

### Stats

`stats` includes monotonic counters, gauges and `messagesPerSecond`, an array of
60 one-second buckets. `collectedAt` is the server timestamp.

## Internal cluster API

These endpoints are for node-to-node traffic only and are signed with
`X-NextMQTT-Timestamp` and `X-NextMQTT-Signature` (hex HMAC-SHA256 over
`timestamp.body`):

- `POST /internal/cluster/heartbeat`
- `POST /internal/cluster/publish`
- `POST /internal/cluster/leave`
- `POST /internal/cluster/retained`
- `POST /internal/cluster/session/claim`

They are not covered by admin token authentication. Block them at the edge.
