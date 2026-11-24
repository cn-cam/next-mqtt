# FAQ

## Is NextMQTT a fork or reimplementation of another broker?

No. It is an independent clean-room implementation of the OASIS MQTT 3.1.1 and
5.0 specifications. It contains no source code, documentation text,
configuration format, logo or trademark from another MQTT broker, and it claims
no affiliation or certification.

## Is it production ready?

The protocol, authentication, ACL and clustering paths are covered by unit and
loopback integration tests, and the container image is built in CI. The main
production gap is durability: sessions, queues and retained messages are
in-memory and do not survive a restart. Use it for workloads that can tolerate
reconnect and re-subscribe, or wait for the persistence roadmap item.

## Why .NET?

The .NET runtime provides mature async sockets, `System.IO.Pipelines`,
high-performance UTF-8 and cryptographic primitives, and a single-file
deployment model. The broker and API run in one process without a VM or an
external database.

## Which MQTT clients work?

Any compliant MQTT 3.1.1 or 5.0 client library. Connect to:

- `mqtt://host:1883`
- `mqtts://host:8883` (when TLS is enabled)
- `ws://host:8083/mqtt`
- `wss://host:8084/mqtt` (when HTTPS is configured)

The bundled `nextmqtt` CLI is also a client and is useful for smoke tests.

## How many connections can one node handle?

It depends on payload sizes, QoS, subscription counts and hardware. The design
targets tens of thousands of idle or low-rate clients per node with modest CPU.
Measure with your own workload using `nextmqtt bench` and watch memory for
offline queues and retained payloads. There is no fixed connection cap in the
code.

## Does clustering guarantee exactly-once delivery?

No. MQTT QoS applies between a client and the broker. Cross-node forwarding is
best-effort HTTP with a deduplication cache. A peer outage can lose messages
routed to it. Local QoS 1/2 semantics are preserved.

## Can I use shared subscriptions across nodes?

Each node evaluates its own shared groups. A message may be handled once per
node, not once per cluster. Global coordination is on the roadmap. For strict
single-consumer semantics, route the topic to one node or use a single shared
group with a single node.

## Where is state stored?

In memory. There is no database, WAL or volume requirement in 0.1. A restart
clears sessions, queues and retained messages. Configuration and secrets live in
environment variables, Compose files or Kubernetes objects.

## How do I reset the admin password?

Set `NEXTMQTT_ADMIN_PASSWORD` and restart. If you use `Admin:PasswordHash`,
generate a new hash:

```bash
dotnet run --project src/NextMqtt.Cli -- hash-password --password 'new-password'
```

## Why did my dashboard token stop working after a restart?

No `NEXTMQTT_ADMIN_TOKEN_SECRET` was configured, so the broker generated an
ephemeral signing key. Set a stable secret on every node.

## Why can't clients connect after I disabled anonymous access?

You need at least one MQTT credential source:

- local users under `Authentication:Users`, or
- a JWT secret under `Authentication:Jwt:Secret`.

The dashboard admin password is not an MQTT credential.

## Why does SUBSCRIBE return `0x87`?

The ACL denied the filter. Subscribe rules must *cover* the requested filter.
See [Authentication & ACL](/authentication#semantics).

## Why did my retained message disappear?

- It was published with an empty payload (deletes retained state).
- Its message expiry interval elapsed.
- It was published on a node that was down on other peers, so it was never
  replicated.
- A later publish replaced it.

## Can I use the dashboard on a different origin?

Add the origin to `Server:Http:AllowedOrigins`. The Vite dev server runs on
`http://localhost:5173` by default. CORS is not a substitute for authentication.

## How are ports configured in containers?

Use environment variables, for example:

```bash
NEXTMQTT_SERVER__HTTP__PORT=9090
NEXTMQTT_SERVER__TCP__PORT=11883
```

Remember to publish the same container ports in Docker/Kubernetes.

## Does NextMQTT phone home or require a licence key?

No telemetry, no licence server, no external dependency at runtime. The project
is Apache-2.0.

## How do I report a security issue?

Use GitHub private vulnerability reporting, not a public issue. See
[SECURITY.md](https://github.com/cn-cam/next-mqtt/blob/main/SECURITY.md).
