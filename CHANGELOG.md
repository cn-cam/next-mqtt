# Changelog

All notable changes to NextMQTT are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Planned

- Durable session and retained-message stores.
- Connection and message rate limiting.
- Enhanced authentication (`AUTH`) providers.
- Global shared-subscription coordination.

## [0.1.1] - 2026-11-24

### Fixed

- Anonymous MQTT clients are no longer accepted when `AllowAnonymous` is false.
  Anonymous access now requires an explicit opt-in.
- Cluster shutdown is idempotent when the host stops and disposes the broker.
- ACL wildcard rules no longer cover leading-`$` topics such as `$SYS/#`.
- Certificate loading uses the non-obsolete `X509CertificateLoader`.
- Documentation titles no longer repeat the product name.
- CLI nullability warnings and the dashboard publish history are cleaned up.

## [0.1.0] - 2026-11-24

### Added

- MQTT 3.1.1 and 5.0 codec with strict validation and malformed-packet tests.
- TCP, TLS, WebSocket and secure WebSocket listeners.
- QoS 0/1/2 in both directions, inflight windows and retransmission.
- Persistent sessions, session expiry, takeover and offline queues.
- Retained messages with message expiry and wildcard retrieval.
- Wills with delay, suppression on clean disconnect and replica support.
- Shared subscriptions with per-group round-robin.
- MQTT 5 subscription options, properties and inbound topic aliases.
- PBKDF2 password authentication, HS256 JWT and anonymous access.
- Ordered topic ACL with identity placeholders and superusers.
- Signed gossip cluster membership and cross-node publish routing.
- Retained-message replication across cluster nodes.
- REST management API with paginated clients, subscriptions and retained state.
- Vue 3 + TypeScript dashboard with English/Chinese UI and dark/light themes.
- CLI with `pub`, `sub`, `bench` and `hash-password` commands.
- Multi-architecture container image with health check.
- Docker Compose single-node and three-node cluster definitions.
- Kustomize manifests with StatefulSet, PDB, HPA, Ingress and NetworkPolicy.
- Helm chart with configurable auth, cluster, resources and ingress.
- Prometheus metrics exposition and JSON statistics.
- VitePress documentation site with English and Chinese guides.
- GitHub Actions for CI, CodeQL, releases and GitHub Pages.
- Mechanical brand/trademark compliance scanner and repository policy.

[Unreleased]: https://github.com/cn-cam/next-mqtt/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.1.1
[0.1.0]: https://github.com/cn-cam/next-mqtt/releases/tag/v0.1.0
