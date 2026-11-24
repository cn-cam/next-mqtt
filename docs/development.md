# Development

## Toolchain

| Tool | Version |
| --- | --- |
| .NET SDK | 10.0.100+ |
| Node.js | 22+ (24 recommended) |
| npm | 10+ |
| Docker | optional, for image and cluster tests |
| Helm | optional, for chart rendering |

`global.json` pins the SDK feature band and allows a newer major SDK for local
development.

## Build and test

```bash
dotnet restore NextMqtt.slnx
dotnet build NextMqtt.slnx -c Release
dotnet test NextMqtt.slnx -c Release
```

The test suites are:

| Project | Scope |
| --- | --- |
| `NextMqtt.Core.Tests` | Codec round trips, malformed packets, topic matching, tries |
| `NextMqtt.Broker.Tests` | Sessions, queues, retained messages, auth, ACL, cluster routing |
| `NextMqtt.Server.Tests` | Real loopback TCP integration tests with the shared codec |

Integration tests start a broker on an ephemeral port and exercise CONNECT,
QoS 0/1/2, retained messages, wills, persistent sessions, takeover, ACL and
authentication.

## Dashboard

```bash
cd web
npm install
npm run dev        # http://localhost:5173
npm run typecheck
npm run build
```

Vite proxies `/api`, `/healthz`, `/readyz` and `/metrics` to
`http://localhost:8080`.

The UI is Vue 3 + TypeScript with:

- hand-written CSS (no component framework),
- a small SVG icon set,
- a lightweight reactive i18n layer (English and Chinese),
- polling composables instead of a websocket dashboard channel.

## Documentation

```bash
cd docs
npm install
npm run dev        # http://localhost:5173 (VitePress default)
npm run build
```

Set `DOCS_BASE=/next-mqtt/` when building for GitHub Pages.

## Repository layout

```text
src/NextMqtt.Core        codec and topic primitives (no dependencies)
src/NextMqtt.Broker      sessions, routing, auth, ACL, retained, cluster
src/NextMqtt.Server      host, listeners, REST API, static dashboard
src/NextMqtt.Cli         pub/sub/bench/hash commands
tests/                   unit and integration tests
web/                     Vue 3 dashboard
docs/                    VitePress documentation
deploy/                  Docker, Compose, Kubernetes and Helm
scripts/                 compliance and maintenance scripts
```

## Adding a protocol feature

1. Add or extend the packet model in `src/NextMqtt.Core/Protocol`.
2. Update the decoder and encoder, including flag and property validation.
3. Add round-trip and malformed-input tests.
4. Wire the behaviour into `ClientConnection` or `BrokerSession`.
5. Cover it with a loopback integration test in `NextMqtt.Server.Tests`.
6. Update `docs/protocol.md` and `CHANGELOG.md`.

Non-obvious behaviour should cite the relevant OASIS MQTT specification
section in a comment.

## Conventions

- `net10.0`, nullable enabled, implicit usings.
- File-scoped namespaces, 4-space indentation, `_camelCase` fields.
- Async methods end in `Async` and accept a `CancellationToken`.
- No `.Result`, `.Wait()` or `Thread.Sleep` on request paths.
- Bounded queues and channels; surface drops in metrics.
- Prefer `sealed` classes and immutable options.
- Dashboard components use `<script setup lang="ts">` and typed props.

## Compliance gate

Every commit must pass:

```bash
./scripts/compliance-check.sh     # Linux/macOS/Git Bash
./scripts/compliance-check.ps1    # Windows
```

The scanner assembles the forbidden brand token at runtime and checks file
names, tracked and untracked contents, commit messages, branches and tags. It is
also a required CI job.

There are no exclusions for comments, tests or documentation. If a term
collides, rename it.

## Runtime dependencies

The runtime and broker have **no third-party NuGet dependencies**. Adding one
requires an architecture decision record in `docs/adr/` and an update to
`docs/legal/third-party-licenses.md`.

## Commit style

Conventional Commits, imperative, 72-character subject:

```text
feat(broker): support message expiry on retained delivery
fix(web): keep the session table stable while polling
docs: document the cluster secret rotation caveat
```

Each commit should build and pass tests. Pull requests should include tests and
documentation updates for behaviour changes.

## Manual verification recipes

```bash
# start a broker
dotnet run --project src/NextMqtt.Server

# subscribe in one shell
dotnet run --project src/NextMqtt.Cli -- sub --topic test/# --count 1

# publish in another
dotnet run --project src/NextMqtt.Cli -- pub --topic test/hello --message hi --qos 1
```

Cluster:

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml up -d --build
# wait for healthy, then run the two CLI commands against ports 1883 and 1884
```

## Code review checklist

- [ ] No forbidden brand tokens
- [ ] Protocol changes covered by encoder/decoder and integration tests
- [ ] Bounded queues and timeouts preserved
- [ ] Errors mapped to the right MQTT reason codes
- [ ] Dashboard types match the API contract
- [ ] Docs, changelog and examples updated
- [ ] No secrets or personal tokens committed
