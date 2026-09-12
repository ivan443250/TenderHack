# Web shell

React + TypeScript + Vite is presentation-only. `src/api/client.ts` and `src/api/sse.ts` are the browser backend boundaries; the browser does not call Knowledge directly or invent lifecycle state.

The current `App` is still a minimal placeholder, so the user-facing chat/product experience described in `../../docs/product-spec.md` and `../../docs/product-experience.md` remains implementation work. Do not mark those surfaces implemented until real server-driven states/actions render and are covered by the relevant tests.

Run `pnpm install`, `pnpm typecheck`, `pnpm test` and `pnpm build` from this directory. Use `pnpm dev` for local development.
