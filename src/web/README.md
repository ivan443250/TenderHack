# Web (React + TypeScript + Vite)

Chat UI for the Portal support case flow, built against `docs/contracts/web-api-v0.md`. The
browser only ever talks to `.NET api` — no direct calls to `knowledge`, and no client-invented
lifecycle state: every screen renders `CaseSnapshot`/`TimelineItem`/SSE events as-is.

## Structure

- `api/` — HTTP client (`client.ts`), SSE helpers (`sse.ts`), wire types (`types.ts`) — 1:1 with
  the contract and `TenderHack.Api.Contracts`.
- `state/caseStore.tsx` — `useCaseSession` hook: fetches a case snapshot and refetches it on every
  SSE event (the snapshot, not the event, is the source of truth).
- `design-system/` — small presentational atoms (Button, IconButton, TextField, chips) matching
  Figma's "01 — Core & Navigation" page.
- `features/navigation`, `features/chat`, `features/home` — the sidebar, chat screen widgets
  (composer, timeline items, handoff card, context panel, request-status stepper,
  resolution/feedback), and the home screen.
- `app/AppShell.tsx` + `App.tsx` — routing (`/` home, `/cases/:caseId` chat) and the shared sidebar.

Styling is Tailwind CSS v4 (`@tailwindcss/postcss`), with design tokens as CSS custom properties in
`src/index.css` (`--content-primary`, `--surface-default`, etc.) mirroring the Figma variables.

Known gaps: the Figma home screen's particle-shader glow relies on an unreleased browser API
("HTML-in-Canvas") and WebGPU, so `features/home/InteractiveGlow` re-implements it in Canvas2D as a
smooth colour field with the shader's cursor physics (its particles are dense enough to read as a
gradient). The
"Materials" tab in the context panel has no backing endpoint in web-api-v0 yet, so it renders a
placeholder. The Portal logo mark and the `FindSans Pro Bold` display font are not committed here
(no export available) — the sidebar uses a text wordmark and the home headline falls back to
Montserrat Bold.

## Local development

```bash
pnpm install
pnpm dev        # Vite dev server on :5173, proxies /api to VITE_API_PROXY (default http://localhost:8080)
pnpm typecheck
pnpm test
pnpm build      # emits dist/
```

`docker compose up` starts a `web` dev-server container (HMR) alongside `api` on 5173/8080.

## Production build

There is no separate production `web` container. `src/support-core/Dockerfile`'s `api` target
builds this package (`pnpm build`) in its own stage and copies `dist/` into the API's `wwwroot`,
which is served same-origin (required for the `owner_id` cookie, web-api-v0.md §13) with an SPA
fallback to `index.html` for client-side routes. The pre-existing manual test console lives at
`/dev-console/` in that same `wwwroot`.
