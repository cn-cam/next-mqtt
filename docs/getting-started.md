# Getting started

NextMQTT is a single broker process with an embedded HTTP API and dashboard.
This page takes you from zero to a working broker and a first MQTT exchange in
a few minutes.

## Requirements

| Mode | Requirements |
| --- | --- |
| Container | Docker 24+ (Compose v2 optional) |
| From source | .NET SDK 10.0.100+, Node.js 22+ (24 recommended) |
| Kubernetes | Kubernetes 1.28+, an Ingress controller (optional) |
| Building images | Docker BuildKit |

## Run with Docker

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 \
  -p 8080:8080 \
  -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  ghcr.io/cn-cam/next-mqtt:latest
```

A Docker Hub mirror is also published:

```bash
docker pull ooooooooocam/next-mqtt:latest
```

Open <http://localhost:8080> and sign in as `admin` with the password you
passed. The dashboard is served by the broker itself; no separate web server is
required.

::: warning Development defaults
The shipped configuration allows anonymous MQTT clients and permissive ACLs so
that the quick start works. Before exposing port 1883 or 8080 to an untrusted
network, follow the [security guide](/security) and set:

```bash
NEXTMQTT_ALLOW_ANONYMOUS=false
NEXTMQTT_ACL__DEFAULTPOLICY=Deny
```
:::

If you do not set an admin password, the broker generates one at startup and
writes it to the log:

```bash
docker logs nextmqtt | grep "bootstrap password"
```

## Run with Compose

A single node:

```bash
cp .env.example .env
# edit .env, then:
docker compose up -d
```

A three-node cluster:

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

| Node | MQTT | Dashboard |
| --- | --- | --- |
| node-a | `localhost:1883` | <http://localhost:8080> |
| node-b | `localhost:1884` | <http://localhost:8081> |
| node-c | `localhost:1885` | <http://localhost:8082> |

Publish on one node and subscribe on another to see cluster routing in action.

## Run from source

```bash
dotnet run --project src/NextMqtt.Server
```

The dashboard bundle is not built by `dotnet run`. For UI development:

```bash
cd web
npm install
npm run dev        # http://localhost:5173, proxies /api to :8080
```

For a production-style local run, build the bundle first:

```bash
cd web && npm run build && cd ..
dotnet run --project src/NextMqtt.Server
```

The server picks up `web/dist` automatically in development and copies it into
`wwwroot` during `dotnet publish`.

## First messages

Use the bundled CLI, which speaks MQTT 5 and MQTT 3.1.1:

```bash
# terminal 1
dotnet run --project src/NextMqtt.Cli -- sub --topic demo/# --qos 1

# terminal 2
dotnet run --project src/NextMqtt.Cli -- pub --topic demo/hello --message "hello" --qos 1
```

The subscriber prints the topic, QoS, retain flag and payload. Add `--retain`
to the publish command, disconnect the subscriber, then subscribe again — the
message is delivered immediately from the retained store.

You can also use any MQTT 3.1.1/5.0 client library against
`mqtt://localhost:1883` or `ws://localhost:8083/mqtt`.

## Ports

| Port | Listener | Configuration |
| --- | --- | --- |
| 1883 | MQTT over TCP | `Server:Tcp` |
| 8883 | MQTT over TLS | `Server:Tls` (disabled by default) |
| 8083 | MQTT over WebSocket at `/mqtt` | `Server:Http:WebSocketPort` |
| 8084 | MQTT over secure WebSocket | `Server:Http:TlsPort` |
| 8080 | REST API, dashboard, `/metrics`, `/healthz` | `Server:Http:Port` |

All listeners bind to `0.0.0.0` by default. In containers, only publish the
ports you actually need.

## Dashboard tour

| Page | What it shows |
| --- | --- |
| Overview | Client/session/subscription gauges, publish throughput, broker counters, node list |
| Clients | Online and persistent offline sessions; disconnect a client |
| Subscriptions | Filters, shared groups, QoS and MQTT 5 subscription options |
| Retained | Stored messages, payload sizes and expiry; delete entries |
| Publish | Publish plain or base64 payloads through the broker |
| Cluster | Membership, node health and topology |
| Settings | Effective listeners and limits, theme, language and token actions |

The dashboard polls the API every few seconds. The header shows when data was
last refreshed.

## Next steps

- [Configuration](/configuration) — every option and environment variable
- [Authentication & ACL](/authentication) — users, JWT and topic rules
- [Deployment](/deployment) — Docker, Compose, Kubernetes and Helm
- [Clustering](/clustering) — three-node setup and routing model
- [Operations](/operations) — metrics, health checks and troubleshooting
