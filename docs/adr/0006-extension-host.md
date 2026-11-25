# ADR 0006: First-party extension host

- Status: proposed
- Date: 2026-11-25

## Context

ADR 0005 decided that 0.1.x ships interfaces instead of a plugin ABI, and that a
first-party extension host needs its own decision. Operators keep asking for a
way to add authentication, routing and integration logic without forking the
broker. Loading plugin packages built for another broker stays out of scope
(clean-room rule and untestable compatibility surface).

## Decision

1. Build a **first-party host**, not a compatibility layer. It defines its own
   manifest, contract and lifecycle.
2. Extensions run **in-process** in a collectible `AssemblyLoadContext`, loaded
   from a configured directory. They implement the existing interfaces
   (`IClientAuthenticator`, `ITopicAuthorizer`, `IClusterRouter`) plus new ones we
   add deliberately.
3. The contract is versioned: a plugin declares `contractVersion`; the host
   refuses to load an unknown major version instead of guessing.
4. Plugins are isolated by convention, not by a security boundary. The manifest
   must state that a plugin runs with broker privileges; the host never claims
   sandboxing.
5. Failure policy: a plugin that throws during startup is unloaded and logged; a
   plugin that throws per request is disabled after a configurable failure count
   so one bad extension cannot take the broker down.

## Consequences

- Extensions no longer require rebuilding the broker, but they do require a
  restart to load, unload or upgrade.
- The plugin surface becomes a supported API we must version and test.
- Out-of-process plugins (separate process, gRPC or HTTP contract) remain
  possible later; this ADR only covers the in-process host.
- Until the host ships, extension points stay source-level interfaces as
  documented in ADR 0005.

## Follow-up

1. Add `NextMqtt.Extensions.Abstractions` with the versioned interfaces.
2. Implement the loader, manifest validation and failure isolation.
3. Document packaging, signing and the upgrade procedure.
