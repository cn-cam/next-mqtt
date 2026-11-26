---
name: api-console
description: Keep the dashboard API console, docs/api.md and the typed API client in sync. Use whenever an HTTP endpoint, request shape or response type changes.
---

# API console and surface gate

The dashboard ships an API console at `/api-console` (System → API console). It
sends real requests to the broker with the signed-in session and is the manual
verification tool for `docs/api.md`.

## Mandatory workflow

Every endpoint in `docs/api.md` must appear in `web/src/api/endpoints.ts` with
the same method and path. When you add, rename or remove an endpoint, update all
of the following in one commit:

1. `src/NextMqtt.Server/Api/` (the endpoint itself);
2. `docs/api.md` (endpoint table and the request/response example);
3. `web/src/api/endpoints.ts` (console entry, auth flag and body template);
4. `web/src/api/client.ts` and `web/src/api/types.ts` when the dashboard calls it;
5. the corresponding i18n strings in `web/src/i18n/index.ts` when the console
   needs a new label.

Run the surface check before committing:

```bash
node scripts/api-surface-check.mjs
```

The script fails when an endpoint is documented but missing from the console, or
present in the console but not documented. CI runs it in the compliance job.

## Console behaviour

- Authenticated endpoints attach `Authorization: Bearer` from `localStorage`.
- `POST` bodies are JSON; invalid JSON is rejected before sending.
- `/metrics` is displayed as text; JSON responses are pretty-printed.
- Path templates such as `/api/v1/clients/{clientId}` and query examples such as
  `/api/v1/retained?topic=...` are edited in the path field.
- The page must remain usable on a narrow screen (single-column layout).
