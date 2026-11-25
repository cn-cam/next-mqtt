# ADR 0008: RabbitMQ bridge with a second reviewed dependency

- Status: accepted
- Date: 2026-11-25

## Context

ADR 0007 approved `Confluent.Kafka` for the Kafka bridge. Operators also run
RabbitMQ and want MQTT topics routed into a topic exchange. Writing AMQP 0-9-1
by hand (frames, channels, exchange declaration, publisher confirms, heartbeats)
is far more error-prone than the NATS text protocol.

## Decision

1. Add `RabbitMQ.Client` 7.3.0, licensed `Apache-2.0 OR MPL-2.0`. The project
   takes it under **Apache-2.0**, which matches the repository licence and needs
   no extra obligations.
2. The bridge follows the same shape as NATS and Kafka: topic filter, bounded
   queue, background worker, origin-only forwarding in a cluster.
3. Messages are published to a durable `topic` exchange with
   `BasicProperties.Persistent = true` and **publisher confirms enabled**, so a
   message counts as forwarded only after RabbitMQ accepts it.
4. A missing broker never blocks startup: the worker retries every two seconds
   and re-declares the exchange on reconnect.
5. Kafka and RabbitMQ can be enabled together; both are optional and independent.

## Consequences

- Two runtime NuGet dependencies now exist, both recorded in NOTICE and
  `docs/legal/third-party-licenses.md`.
- The routing key mapping matches NATS (`/` → `.`, unsafe characters → `_`), so
  one mental model covers all integration bridges.
- RabbitMQ restart with a durable exchange and persistent messages keeps queued
  data; consumers remain responsible for their own queue bindings.

## Follow-up

1. Add the package and licence rows.
2. Document `Integrations:Rabbit`.
3. Keep the guarded smoke test (`NEXTMQTT_RABBIT_URL`) in the suite so a real
   broker can be verified on demand.
