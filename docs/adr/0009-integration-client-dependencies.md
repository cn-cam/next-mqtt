# ADR 0009: Keep the integration clients as packages, do not vendor them

- Status: accepted
- Date: 2026-11-25

## Context

The Kafka and RabbitMQ bridges depend on `Confluent.Kafka` (ADR 0007) and
`RabbitMQ.Client` (ADR 0008). Both licences permit copying the source into the
repository:

| Package | Licence | Source vendoring feasible? |
| --- | --- | --- |
| `Confluent.Kafka` | Apache-2.0 | Only the C# wrapper. The protocol work lives in `librdkafka`, a native library shipped per platform as `librdkafka.redist` under BSD-2-Clause. |
| `RabbitMQ.Client` | Apache-2.0 OR MPL-2.0 | Yes: pure managed C#, so the source can be copied. |

The request was to pull the sources in and treat them as local libraries so the
implementation is "source-controlled and has no third-party dependency".

## Decision

Keep both as pinned NuGet dependencies and do **not** vendor them.

1. **Vendoring Kafka would not deliver the goal.** Copying the wrapper while
   still loading a native `librdkafka` binary gives the appearance of source
   control without the substance: the code that actually speaks the protocol
   stays a binary blob and must still be built or downloaded per architecture.
2. **Vendoring RabbitMQ.Client moves security patching onto this project.**
   The AMQP client is large, has its own dependency floor and ships fixes for
   protocol and security issues. A fork would have to track those fixes by hand,
   with no functional benefit over a version-pinned package.
3. **The supply chain is already controlled.** Versions are centralised in
   `Directory.Packages.props`, the licence obligations are recorded in `NOTICE`
   and `docs/legal/third-party-licenses.md`, and the bridges are optional: with
   both disabled, the broker has no third-party runtime dependency at all.
4. **Both are replaceable behind our own seam.** The bridges only touch the
   client through a small surface (`ProduceAsync`, `BasicPublishAsync`), so a
   future in-house client can replace a package without touching routing.

## Consequences

- Upgrading a client is a version bump in one file, not a source merge.
- `dotnet restore` needs a NuGet feed; offline builds use a local mirror
  (`NEXTMQTT_NUGET_MIRROR`) rather than a vendored tree.
- Reviewers can audit the exact versions from `Directory.Packages.props`, and
  the compliance workflow keeps brand and licence files honest.
- If a future requirement demands a fully source-visible build, the path is to
  vendor `RabbitMQ.Client` only (it is pure managed) and to keep Kafka behind a
  separately reviewed native package; this ADR would be superseded at that
  point.

## Follow-up

1. Keep the versions pinned; do not float ranges.
2. Re-check the licences when either package changes its licence model.
3. Revisit if a customer requires a build with zero external runtime binaries.
