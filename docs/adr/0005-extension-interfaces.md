# ADR 0005: Extension interfaces, no plugin ABI

- Status: accepted
- Date: 2026-11-24

## Context

Operators moving from another MQTT platform often ask for "plugin support",
meaning the ability to drop in packages built for that platform (custom
authentication, storage backends, bridges, rule engines).

Those packages are compiled against the internal assembly layout, lifecycle and
configuration schema of the broker they were written for. Offering a compatible
plugin host would mean reproducing that layout and lifecycle, which the
clean-room rule (AGENTS.md §2) forbids, would import a large compatibility
surface we cannot test, and would freeze our own internals at version 0.1.

At the same time, some extension needs are real: custom credential stores,
authorisation policies, and routing strategies.

## Decision

1. NextMQTT 0.1.x ships **no plugin ABI** and does **not** load plugin packages
   written for other MQTT brokers. Nothing in the product claims that it does.
2. Extension happens through a small set of interfaces in
   `NextMqtt.Broker`, resolved from dependency injection:

   | Extension point | Interface | Purpose |
   | --- | --- | --- |
   | Client authentication | `IClientAuthenticator` | Replace or extend the built-in username/password and API-key checks |
   | Topic authorisation | `ITopicAuthorizer` | Allow or deny publish/subscribe per client |
   | Cluster routing | `IClusterRouter` | Replace the HTTP forwarding strategy |

3. Integrations that must stay out of process use the management REST API and
   the cluster HTTP endpoints, which are versioned (`/api/v1`, `/internal/cluster`).
4. A first-party extension host (out-of-process, versioned contract) needs its
   own ADR before any code lands. Until then, custom behaviour is added by
   changing source and rebuilding; the container image and Helm chart are
   unaffected.

## Consequences

- No compatibility surface to maintain, and no ambiguity about what "plugin"
  means in this project.
- Extending the broker requires a build from source; this is stated in
  `docs/features.md` and `docs/roadmap.md`.
- Internals stay free to change between releases.
- If a first-party host ships later, it will define its own contract rather
  than adopt a foreign one.
