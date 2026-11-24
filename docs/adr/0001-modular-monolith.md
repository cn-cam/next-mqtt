# ADR 0001: Modular monolith, no runtime NuGet dependencies

- Status: accepted
- Date: 2026-11-24

## Context

The broker needs high-throughput networking, protocol framing, HTTP APIs and a
dashboard. Splitting these into independently deployed services would add
operational cost and latency. Many MQTT platforms also require a runtime VM or
external database.

## Decision

Ship one ASP.NET Core process composed of four assemblies:

- `NextMqtt.Core` — protocol and topics.
- `NextMqtt.Broker` — broker engine.
- `NextMqtt.Server` — host and management plane.
- `NextMqtt.Cli` — client tooling.

The runtime uses only the .NET 10 shared frameworks. Third-party NuGet packages
are limited to test tooling. New runtime dependencies require an ADR and a
licence review.

## Consequences

- Simple deployment: one image, no external database.
- Explicit module boundaries via project references and namespaces.
- No plugin ABI in 0.1; extensions require source changes.
- The dashboard is built separately and copied into `wwwroot`.
