# Security model

NextMQTT is an independent, clean-room implementation. Security reports and
hardening questions are welcome through the repository's private advisory
channel.

## Trust boundaries

| Boundary | Protection |
| --- | --- |
| MQTT clients → broker | Optional TLS, password/JWT authentication, topic ACL |
| Browser → dashboard | Bearer token or API key, optional HTTPS |
| Node → node | HMAC-SHA256 signed requests, shared secret, optional HTTPS |
| Operator → container | Non-root UID 10001, dropped capabilities, optional read-only root filesystem |
| Build → release | CI compliance scan, CodeQL, pinned dependencies, OCI provenance |

## Hardening checklist

### Credentials

- [ ] Set a strong `NEXTMQTT_ADMIN_PASSWORD` or pre-hashed
      `Admin:PasswordHash`.
- [ ] Set `NEXTMQTT_ADMIN_TOKEN_SECRET` to a random value shared by all nodes.
- [ ] Never commit `.env`, certificates, private keys or API keys.
- [ ] Rotate the admin password and token secret after a suspected exposure.
- [ ] Use hardware-backed secret stores where available.

Generate secrets:

```bash
openssl rand -base64 32
```

### MQTT access

- [ ] Disable anonymous access.
- [ ] Deny by default and add explicit ACL rules.
- [ ] Require TLS for clients outside a trusted network.
- [ ] Set per-session limits appropriate to your memory budget.
- [ ] Consider a gateway that enforces connection rate limits.

### Dashboard

- [ ] Put the dashboard behind HTTPS and network restrictions.
- [ ] Reduce `Admin:TokenLifetimeMinutes` if tokens are high-value.
- [ ] Protect `/metrics` with `Admin:MetricsRequireAuth` if exposed.
- [ ] Do not expose `/internal/cluster/*` publicly.
- [ ] Restrict CORS to your dashboard origins.

### Cluster

- [ ] Use a unique 16+ character secret per environment.
- [ ] Keep clocks synchronised (NTP) within five minutes.
- [ ] Use HTTPS advertise URLs outside a private network.
- [ ] Block internal cluster paths at the ingress.
- [ ] Rotate secrets during a maintenance window.

### Containers and Kubernetes

- [ ] Run as UID 10001 (the image does by default).
- [ ] Keep `allowPrivilegeEscalation: false` and drop all capabilities.
- [ ] Set CPU/memory limits.
- [ ] Use a read-only root filesystem with writable `/tmp` and `/app/data`.
- [ ] Apply the NetworkPolicy or stricter egress rules.
- [ ] Use `existingSecret` / external secret operators for credentials.

## Authentication details

### Password hashing

PBKDF2-SHA256 with a random 16-byte salt and 210,000 iterations by default. The
encoded hash includes its iteration count, and verification uses a constant-time
comparison.

### JWT

Only HS256 is accepted. Tokens must contain `exp` by default, are checked for
`nbf`, `iss` and `aud`, and the `sub` claim becomes the principal. The shared
secret is configured through `Authentication:Jwt:Secret`.

Do not reuse the admin token secret, cluster secret and JWT secret.

### Admin tokens

Dashboard tokens are HS256 JWTs signed with `Admin:TokenSecret`. They carry the
`admin` role and are accepted only by the REST API middleware. API keys are
matched by SHA-256 digest; treat them as bearer credentials with the same power
as an admin token.

## Data handling

- Payloads are treated as opaque bytes and are not logged by default.
- MQTT user properties and metadata are preserved; do not put secrets in topic
  names or user properties.
- The dashboard displays topic names and payload sizes, not payload contents.
- Sessions, queues and retained messages are in memory. A crash dump can
  contain them; restrict access accordingly.
- The cluster deduplication cache holds message ids for up to ten minutes.

## Denial-of-service considerations

The broker bounds packet size, inflight windows, offline queues and the outbound
channel; it also enforces a CONNECT timeout and keep-alive. It does **not**
implement per-IP connection limits or message-rate quotas. Protect the listener
with:

- network policies and load balancer limits;
- connection rate limiting at the edge;
- authentication before large payloads are accepted;
- resource limits in containers.

## Vulnerability reporting

Use GitHub private vulnerability reporting:

<https://github.com/cn-cam/next-mqtt/security/advisories/new>

Include the version, deployment mode, reproduction steps and impact. Do not
open a public issue for an unpatched vulnerability.

## Independence and trademarks

This project contains no source code, documentation text, config format, logo or
trademark from any other MQTT broker product. Brand-name rules are enforced in
CI; see [AGENTS.md](https://github.com/cn-cam/next-mqtt/blob/main/AGENTS.md) and
[TRADEMARKS.md](https://github.com/cn-cam/next-mqtt/blob/main/TRADEMARKS.md).
