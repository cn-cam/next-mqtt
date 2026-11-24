# ADR 0004: Vue 3 without a component framework

- Status: accepted
- Date: 2026-11-24

## Context

The dashboard needs tables, charts, forms, dialogs and a theme. A component
framework would speed up assembly but adds a large dependency surface, visual
constraints and upgrade risk.

## Decision

Build the dashboard with Vue 3, TypeScript, Vue Router and Pinia only. Layout,
tables, charts and dialogs are hand-written CSS/SVG components in `web/src`.

- No external icon font or chart library.
- Polling composables instead of a websocket dashboard channel.
- A tiny reactive i18n module with English and Chinese dictionaries.

## Consequences

- Small bundle and no UI framework upgrade churn.
- More custom CSS and accessibility work owned by this project.
- Visual consistency is enforced through `web/src/styles/main.css` tokens.
- Charts are intentionally simple (sparkline) and can be extended later.
