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
| PUBLISH | ✅ | ✅ | QoS 0/1/2, retain, DUP, topic alias (inbound) |
| PUBACK | ✅ | ✅ | Reason code and properties in 5.0 |
| PUBREC / PUBREL / PUBCOMP | ✅ | ✅ | Full QoS 2 handshake |
| SUBSCRIBE | ✅ | ✅ | Wildcards, shared subscriptions, subscription identifier |
| SUBACK | ✅ | ✅ | Per-filter reason codes |
| UNSUBSCRIBE / UNSUBACK | ✅ | ✅ | Per-filter reason codes in 5.0 |
| PINGREQ / PINGRESP | ✅ | ✅ | Keep-alive |
| DISCONNECT | ✅ | ✅ | Reason code, session expiry update, will-on-disconnect |
| AUTH | — | ⚠️ | Rejected with `0x8C`; enhanced auth is not configured |

## Features

### Sessions

- Clean start and persistent sessions.
- MQTT 5 session expiry interval, including updates in DISCONNECT.
- Session takeover: the previous connection receives DISCONNECT `0x8E`.
- Offline queue for persistent sessions, bounded by `Broker:MaxQueuedMessages`.
- Broker-assigned client identifiers for zero-length ids (returned in
  `AssignedClientIdentifier`).

### Subscriptions

- `+` and `#` wildcards with the leading-`$` exclusion rule.
- Shared subscriptions: `$share/{group}/{filter}`, round-robin per filter and
  group.
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
- Inbound topic aliases (up to `Broker:MaxTopicAlias`); outbound aliases are not
  used yet.
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

- Enhanced authentication (`AUTH` exchange) and re-authentication.
- Outbound topic alias assignment.
- MQTT over QUIC.
- Cross-node shared-subscription coordination.
- Durable storage across broker restarts.
- Bridges, rule engines and plugin APIs.

See the [roadmap](/roadmap) for planned work.
