# Protocol support

NextMQTT implements MQTT 3.1.1 (protocol level 4) and MQTT 5.0 (level 5) from
the OASIS specifications.

## Transports

| Transport | Default port | Notes |
| --- | --- | --- |
| TCP | 1883 | Plain MQTT |
| TLS | 8883 | Optional; requires a certificate |
| WebSocket | 8083 (`/mqtt`) | Subprotocol `mqtt` negotiated when requested |
| Secure WebSocket | 8084 (`/mqtt`) | Optional Kestrel HTTPS endpoint |

WebSocket clients connect to `ws://host:8083/mqtt` or `wss://host:8084/mqtt`.
The same HTTP endpoint serves the dashboard and REST API.

## Control packets

| Packet | 3.1.1 | 5.0 | Notes |
| --- | --- | --- | --- |
| CONNECT | ✅ | ✅ | Username/password, will, clean start, session expiry |
| CONNACK | ✅ | ✅ | Session present, reason code, server properties |
| PUBLISH | ✅ | ✅ | QoS 0/1/2, retain, DUP, topic aliases (inbound and outbound) |
| PUBACK | ✅ | ✅ | Reason code and properties in 5.0 |
| PUBREC / PUBREL / PUBCOMP | ✅ | ✅ | Full QoS 2 handshake |
| SUBSCRIBE | ✅ | ✅ | Wildcards, shared subscriptions, subscription identifier |
| SUBACK | ✅ | ✅ | Per-filter reason codes |
| UNSUBSCRIBE / UNSUBACK | ✅ | ✅ | Per-filter reason codes in 5.0 |
| PINGREQ / PINGRESP | ✅ | ✅ | Keep-alive |
| DISCONNECT | ✅ | ✅ | Reason code, session expiry update, will-on-disconnect |
| AUTH | — | ✅ | Challenge/response with pluggable providers; built-in `nextmqtt-jwt` |

## Features

### Sessions

- Clean start and persistent sessions.
- MQTT 5 session expiry interval, including updates in DISCONNECT.
- Session takeover: the previous connection receives DISCONNECT `0x8E`.
- A reconnect with `CleanStart=false` on another node claims the session from
  its owner or recovers it from a replica; inbound QoS 2 publishes waiting for
  PUBREL travel with the session.
- Offline queue for persistent sessions, bounded by `Broker:MaxQueuedMessages`.
- Broker-assigned client identifiers for zero-length ids (returned in
  `AssignedClientIdentifier`).

### Subscriptions

- `+` and `#` wildcards with the leading-`$` exclusion rule.
- Shared subscriptions: `$share/{group}/{filter}`; each message is assigned to
  one member of the cluster-wide group.
- MQTT 5 options: `noLocal`, `retainAsPublished`, retain handling 0/1/2.
- Subscription identifiers attached to matching deliveries.
- Overlapping subscriptions are aggregated per client; one message is delivered
  at the highest matching QoS.

### Messages

- Retained messages, including deletion with an empty payload.
- Message expiry interval for retained and live messages.
- Will messages with will delay; suppressed by a clean DISCONNECT (or published
  explicitly with reason `0x04`).
- MQTT 5 user properties, content type, response topic and correlation data are
  preserved end to end.
- Inbound and outbound topic aliases, bounded by `Broker:MaxTopicAlias` and the
  client's Topic Alias Maximum.
- Maximum packet size enforced per listener via `Broker:MaxPacketSize`.
- Receive maximum advertised in CONNACK; inbound unacknowledged QoS 2 messages
  are bounded per session.

### Keep-alive

A client must send any control packet within 1.5× its keep-alive interval. The
broker closes the connection with reason `0x8D` (MQTT 5) when the interval is
exceeded. Keep-alive `0` disables the check.

## Limits

| Limit | Default | Option |
| --- | --- | --- |
| Packet size | 1 MiB | `Broker:MaxPacketSize` |
| Inflight per session | 32 | `Broker:MaxInflightMessages` |
| Offline queue | 1,000 messages | `Broker:MaxQueuedMessages` |
| Subscriptions per session | 1,000 | `Broker:MaxSubscriptionsPerSession` |
| Topic aliases | 32 | `Broker:MaxTopicAlias` |
| Client id length | 256 | `Broker:MaxClientIdLength` |
| CONNECT timeout | 10 s | `Broker:ConnectTimeout` |
| Retransmit interval | 10 s | `Broker:RetransmitInterval` |

MQTT-level maximums still apply: 268,435,455 bytes remaining length, 65,535
bytes per UTF-8 field, and 65,535 in-flight packet identifiers.

## Error handling

Malformed packets close the connection. With MQTT 5 the broker attempts to send
a DISCONNECT with a specific reason such as:

| Reason | Cause |
| --- | --- |
| `0x81` Malformed packet | Invalid flags, truncated fields, bad UTF-8 |
| `0x82` Protocol error | Invalid state transition, zero packet id |
| `0x8F` Topic filter invalid | Malformed filter in SUBSCRIBE |
| `0x90` Topic name invalid | Empty or wildcard topic in PUBLISH |
| `0x94` Topic alias invalid | Alias outside the negotiated range |
| `0x95` Packet too large | Packet exceeds the configured limit |
| `0x87` Not authorized | ACL denied publish or subscribe |
| `0x8D` Keep-alive timeout | No packet within 1.5× keep-alive |

MQTT 3.1.1 clients receive the closest CONNACK return code on connection
failure and are disconnected on protocol errors.

## Non-goals (current release)

- MQTT over QUIC.
- Cross-node ordering or exactly-once delivery guarantees; cluster forwarding is
  best effort.
- Runtime compatibility with third-party broker plugins or extension packages.
- Asymmetric JWT signatures; only HS256 is accepted.

See the [roadmap](/roadmap) for planned work.
