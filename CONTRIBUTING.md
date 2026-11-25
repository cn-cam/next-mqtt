# Contributing to NextMQTT

Thank you for helping build NextMQTT. This project values small, reviewable
changes, tests for behaviour changes, and strict brand/legal hygiene.

## Before you start

Read [AGENTS.md](AGENTS.md). The brand-name prohibition and clean-room rules
are hard requirements, not guidelines. If you plan to add a dependency, open an
issue first; runtime dependencies need an ADR in `docs/adr/`.

## Development setup

Prerequisites:

- .NET SDK 10.0.100 or newer
- Node.js 22 or newer (24 recommended)
- Docker (optional, for image and cluster testing)

```bash
dotnet restore NextMqtt.sln
dotnet build NextMqtt.sln -c Release
dotnet test NextMqtt.sln -c Release

cd web
npm install
npm run build
```

Run the broker:

```bash
dotnet run --project src/NextMqtt.Server
```

Run the dashboard with hot reload:

```bash
cd web
npm run dev
```

## Making a change

1. Create a branch: `feat/short-description` or `fix/short-description`.
2. Write tests first for protocol or broker behaviour.
3. Keep public APIs documented with XML comments when behaviour is not obvious.
4. Update the relevant page in `docs/` and `CHANGELOG.md` under `Unreleased`.
5. Run the full local gate:

```bash
dotnet test NextMqtt.sln -c Release
./scripts/compliance-check.sh
cd web && npm run typecheck && npm run build
```

6. Commit with a Conventional Commit message and open a pull request.

## Protocol changes

Protocol code lives in `src/NextMqtt.Core/Protocol`. Any new packet or
property must have:

- an encoder test and a decoder test;
- a malformed-input test proving the connection is closed with the correct
  reason code;
- a note in `docs/protocol.md` if the feature changes advertised behaviour.

Use the OASIS specification as the source of truth and cite the relevant
section in a comment.

## Coding style

- File-scoped namespaces, 4-space indentation, `_camelCase` private fields.
- Prefer `sealed` classes and `readonly struct` where practical.
- Async methods end with `Async`; pass `CancellationToken` through.
- Never use `.Result` or `.Wait()` on async code paths.
- Catch only exceptions you can handle; log with structured properties.
- Dashboard components are `<script setup lang="ts">` with typed props.

## Compliance check

The scanner assembles the forbidden token at runtime, so it can never be
copied from the scanner itself. It checks tracked file contents, file names and
the latest commit message. CI runs it on every push and pull request.

If a check fails, rename the identifier or rephrase the text. Do not add
exclusions for prose, comments or tests.

## Review expectations

- Two maintainer approvals for changes touching protocol, auth or cluster
  security.
- No force-push to `main`; use pull requests.
- Keep the commit history readable; squash fixup commits before merge.

## C# documentation gate

GenerateDocumentationFile is enabled for the shipping projects, so the
compiler reports every public member that has no XML documentation (CS1591).
Test projects opt out.

Public members alone are not the whole rule. AGENTS.md section 5.1 also
requires a comment on every private field, helper and nested type, and a
`<remarks>` paragraph on members of publicly visible types. The compiler cannot
check either, so `scripts/docs-coverage.ps1` does:

```bash
pwsh -File scripts/docs-coverage.ps1
```

The script scans `src/**/*.cs`, ignoring comments and string literals, and
fails when a member has no `<summary>`, when a member of a public type has no
`<remarks>`, or when a member carries two stacked documentation blocks. CI runs
it as part of the compliance workflow.

The backlog is at zero. Keep it there: write the comment in the same commit as
the code, and do not paste signature restatements. Say what the member
guarantees, its units, its locking and its failure modes. The bilingual style
is described in `.opencode/skill/csharp-documentation/SKILL.md`.

## Documentation site

The VitePress site is built from `docs/`. Two pages are generated at build time
and must not be edited by hand:

- `docs/changelog.md` comes from `CHANGELOG.md`, so a release note reaches the
  site with the next build;
- `docs/development-log.md` comes from `git log`.

Both are produced by `scripts/docs-site.mjs`, which the docs `build` and `dev`
scripts run automatically, and both are ignored by git.

Translations live in `docs/zh`, `docs/ja` and `docs/ko`. Every English page needs
all three, and `scripts/docs-i18n-check.ps1` fails when a translation falls more
than three points below its recorded coverage. When you translate a page and
raise its coverage, refresh the baseline with `-Update`; when you add English
content without translating it yet, lower the baseline deliberately and say so
in the pull request.