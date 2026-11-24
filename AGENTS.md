# Repository-wide engineering rules

These rules are **hard requirements** for every human and automated contributor
(including AI agents). They override convenience, deadlines and personal
preference. A change that violates them must not be merged.

## 1. Absolute brand-name prohibition

The project must never contain, reference or imply the brand of any existing
commercial MQTT broker.

The prohibited brand token is defined by a private deny-list and reconstructed
at runtime by the compliance scanner; this repository does not store it in
plain form. The token, its case variants, and obvious encodings are
**forbidden**. They must not appear anywhere in this repository, including but
not limited to:

- source code, comments, identifiers, namespaces, class names, log messages;
- configuration files, environment variable names, Docker/Kubernetes resource
  names, Helm values, image names, volume names;
- documentation, diagrams, screenshots, alt text, link slugs, banners;
- test fixtures, sample payloads, benchmark data, commit messages, branch
  names, tag names, release notes and GitHub issue/PR templates;
- generated files that are committed to the repository.

Do not write the token even in a sentence that denies association. Refer to
"third-party MQTT broker products" instead. Do not use it as a comparison
baseline in prose.

Product naming rules:

- The product is **NextMQTT**. NuGet namespaces use `NextMqtt`. Environment
  variables use the `NEXTMQTT_` prefix.
- Never state or imply endorsement, affiliation, compatibility certification
  or sponsorship by any third-party broker vendor.
- Do not copy logos, icons, colour schemes, screenshots, config layouts or
  documentation wording from other brokers.

## 2. Clean-room implementation

- All code in this repository is written from scratch for NextMQTT.
- Copying source, tests, configuration formats or documentation text from any
  other MQTT broker is prohibited, including partial copies and translated
  copies.
- Behaviour may follow the public MQTT specifications (OASIS MQTT 3.1.1 and
  MQTT 5.0), because those are open standards. Cite the specification section
  in comments when a behaviour is non-obvious.
- Third-party libraries are allowed only if their licence is compatible with
  Apache-2.0, and must be recorded in `NOTICE` and
  `docs/legal/third-party-licenses.md`.

## 3. Compliance gate

Before every commit run:

```bash
# Linux / macOS / Git Bash
./scripts/compliance-check.sh

# Windows PowerShell
./scripts/compliance-check.ps1
```

The check is also enforced by CI (`.github/workflows/compliance.yml`). If it
fails, fix the content; never weaken or bypass the check. The scanner
assembles the forbidden token at runtime, so the scanner itself does not
contain it.

If a legitimate technical term ever collides with a forbidden token, rename
the term. There are no exceptions for "just a comment" or "only a test".

## 4. Secrets

- Never commit passwords, tokens, private keys, connection strings or real
  customer data. Use environment variables or Kubernetes Secrets.
- `appsettings.json` may contain development defaults only, and must never
  contain a usable production credential.
- Personal access tokens pasted into an issue or chat must be treated as
  compromised and rotated.

## 5. Code and API conventions

- Target framework: `net10.0`. Nullable reference types and implicit usings are
  enabled for every project.
- Public APIs use `Async` suffixes and accept `CancellationToken` where I/O is
  involved.
- No blocking calls (`.Result`, `.Wait()`, `Thread.Sleep`) on request paths.
- Runtime projects must not take third-party NuGet dependencies without an
  architecture decision record in `docs/adr/`.
- The dashboard is Vue 3 + TypeScript and must build with the pinned Node
  version in `.nvmrc` / CI.
- User-visible strings in the dashboard are English by default; Chinese
  translations live in `web/src/i18n`.

## 6. Commit and review rules

- Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`, `test:`,
  `build:`, `ci:`, `refactor:`, `perf:`), imperative mood, <= 72 character
  subject.
- Every commit must build and pass the compliance check. Do not commit
  commented-out code or local paths.
- Pull requests must update documentation and tests for behaviour changes.
- Reviewers must reject any change that introduces a forbidden token or
  third-party broker branding, regardless of intent.
