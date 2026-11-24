# Authentication & ACL

NextMQTT supports three client authentication modes and an ordered topic ACL.
Dashboard authentication is separate and described at the end of this page.

## Client authentication

`ClientAuthenticator` tries methods in this order:

1. Local username/password (PBKDF2-SHA256).
2. HS256 JWT supplied in the MQTT password field.
3. Anonymous access, if enabled.

A failure returns CONNACK `0x86` (bad username or password). MQTT 3.1.1 clients
receive return code `4`.

### Password users

Users are configured in `Authentication:Users`:

```json
{
  "Authentication": {
    "AllowAnonymous": false,
    "Users": [
      {
        "Username": "device-1",
        "PasswordHash": "pbkdf2-sha256$210000$c2FsdA==$aGFzaA==",
        "Roles": ["devices"]
      }
    ]
  }
}
```

Create a hash:

```bash
dotnet run --project src/NextMqtt.Cli -- hash-password --password 's3cret'
```

Password comparison uses `CryptographicOperations.FixedTimeEquals`. The encoded
format carries its own iteration count, so hashes can be upgraded gradually.

### JWT

Configure a shared secret:

```json
{
  "Authentication": {
    "Jwt": {
      "Secret": "replace-with-at-least-32-random-characters",
      "Issuer": "https://issuer.example",
      "Audience": "nextmqtt",
      "RequireExpiration": true
    }
  }
}
```

A client connects with any username and the JWT as its password:

```text
Username: anything
Password: eyJhbGciOiJIUzI1NiIs...
```

Requirements:

- `alg` must be `HS256`.
- `exp` must be in the future (unless `RequireExpiration` is false).
- `nbf` is honoured with 30 seconds of leeway.
- `iss`/`aud` are validated when configured.
- The `sub` claim becomes the client's username.
- A `roles` claim containing `admin` makes the principal a superuser.

Only HMAC tokens are supported. For asymmetric keys, terminate authentication
in your own gateway and forward a short-lived HS256 token, or extend
`IClientAuthenticator`.

### Anonymous access

```bash
NEXTMQTT_ALLOW_ANONYMOUS=true
```

Anonymous clients receive a principal named after their client id with no ACL
privileges unless the ACL allows them. Anonymous access is convenient for local
development but should be disabled in production.

## Topic ACL

ACL rules live under `Acl`:

```json
{
  "Acl": {
    "DefaultPolicy": "Deny",
    "Rules": [
      { "Effect": "Allow", "Action": "Subscribe", "Topic": "$SYS/#" },
      { "Effect": "Allow", "Action": "Both", "Topic": "devices/${clientid}/#" },
      { "Effect": "Deny", "Action": "Publish", "Topic": "devices/+/command" }
    ]
  }
}
```

### Semantics

- Rules are ordered; the first rule whose action and identity globs match
  decides.
- `Action` is `Publish`, `Subscribe` or `Both`.
- `Username` and `ClientId` accept `*` and `?` globs; omitted means any.
- `Topic` supports `${username}` and `${clientid}` substitution.
- `DefaultPolicy` applies when no rule matches.

Publish authorization evaluates the rule filter against the concrete topic.
Subscribe authorization requires the rule filter to **cover** the requested
filter:

| Rule | Requested filter | Allowed |
| --- | --- | --- |
| `sensors/#` | `sensors/+/temperature` | ✅ |
| `sensors/#` | `sensors/#` | ✅ |
| `sensors/+` | `sensors/#` | ❌ |
| `#` | `$SYS/#` | ❌ (leading-`$` rule) |
| `devices/${clientid}/#` | `devices/device-1/#` | ✅ for client `device-1` |

Retained messages are checked again at delivery time, so a client cannot read a
retained topic that its subscription rule does not cover.

### Superusers

A principal is a superuser when:

- its username appears in `Authentication:Superusers`, or
- its local user roles or JWT roles contain `admin`.

Superusers bypass the ACL entirely. Treat this list as part of your
administrative surface.

## Dashboard authentication

The dashboard uses a separate credential store:

- `NEXTMQTT_ADMIN_PASSWORD` is hashed at startup.
- `POST /api/v1/auth/login` returns an HS256 token valid for
  `Admin:TokenLifetimeMinutes`.
- Tokens are stored in browser `localStorage` and sent as `Authorization:
  Bearer`.
- `X-API-Key` is accepted for automation when its SHA-256 digest is listed in
  `Admin:ApiKeySha256`.

Dashboard tokens are **not** MQTT credentials. They only authorize the REST API.

### Bootstrap password

If no password is configured:

- the broker generates a random password,
- hashes it in memory,
- writes the plaintext once to the warning log.

The password is not stored on disk. Restarting the broker without a configured
password generates a new one. Set `NEXTMQTT_ADMIN_PASSWORD` or
`Admin:PasswordHash` for stable deployments.

### Token security

- Set `NEXTMQTT_ADMIN_TOKEN_SECRET` to the same random value on every node.
- Tokens are bearer credentials; use HTTPS for the dashboard.
- Reduce `TokenLifetimeMinutes` in high-security environments.
- Sign-out only removes the browser copy; rotate the token secret to invalidate
  every outstanding token.

## Threat model notes

- The cluster secret authenticates nodes, not clients. Keep it out of images
  and logs.
- MQTT payloads are opaque bytes; payload format indicators are validated only
  for structural correctness.
- The broker does not implement rate limiting per client. Use connection limits
  at the edge or extend the connection handler.
- TLS certificate validation for optional client certificates uses platform
  defaults; mutual TLS policy is deployment-specific.
