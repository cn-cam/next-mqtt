# ADR 0003: In-memory broker state for 0.1

- Status: accepted
- Date: 2026-11-24

## Context

Persistence choices (SQLite, PostgreSQL, a WAL, a replicated log) strongly affect
performance, deployment and failure semantics. Shipping a half-defined storage
contract would be harder to change later than shipping an explicit in-memory
implementation.

## Decision

Sessions, offline queues and retained messages are stored in memory. The broker
exposes storage-shaped interfaces at the engine boundary where practical, but
0.1 does not promise durability across restarts.

Documentation and release notes state this limitation prominently.

## Consequences

- No volumes or database required; simple one-command deployment.
- Rolling restarts drop sessions, queues and retained state.
- Clients must reconnect and re-subscribe after a restart.
- Persistence will be introduced behind an explicit store interface with
  migration and backup documentation.
