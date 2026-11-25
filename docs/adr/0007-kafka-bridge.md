# ADR 0007: Kafka bridge with a client dependency

- Status: accepted
- Date: 2026-11-25

## Context

ADR 0001 allows runtime NuGet packages only with an ADR and a licence review.
The NATS bridge needed no dependency because its protocol is a text command set.
Kafka is a binary protocol with version negotiation, metadata discovery, batching
and compression; reimplementing the producer correctly is weeks of work and a
maintenance liability.

## Decision

1. Add `Confluent.Kafka` (Apache-2.0) as the only runtime NuGet dependency. It is
   used by `NextMqtt.Broker` for the Kafka bridge only.
2. The bridge reuses the existing integration shape: a topic filter, a bounded
   queue, one background producer, origin-only forwarding in a cluster, and the
   same counters/logging as the webhook and NATS bridges.
3. Configuration lives under `Integrations:Kafka`
   (`BootstrapServers`, `TopicPrefix`, `Filter`, `MaxQueue`, `ClientId`,
   `LingerMs`, `Compression`).
4. The broker starts without Kafka when `Enabled` is false or the client cannot
   reach the cluster; the bridge retries in the background instead of failing
   startup.
5. Image size: the client adds roughly 3 MB to the runtime image. That is
   acceptable and recorded in `docs/legal/third-party-licenses.md`.

## Consequences

- The "no runtime dependencies" statement becomes "one reviewed dependency for
  the Kafka bridge"; NOTICE and the licence inventory must list it.
- The bridge is optional: deployments that do not enable it pay only image size.
- Kafka protocol upgrades are delegated to the client library.
- If the dependency becomes unmaintained, the bridge can be removed without
  touching the broker core.

## Follow-up

1. Add the package and a licence-table entry.
2. Implement the producer, mapping MQTT topics to Kafka topics with the same
   dot/underscore rules as NATS.
3. Add an integration test guarded by `NEXTMQTT_KAFKA_BOOTSTRAP`, plus a unit
   test for configuration and topic mapping.
