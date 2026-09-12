# Web shell scaffold

React + TypeScript + Vite is presentation-only. `src/api/client.ts` and `src/api/sse.ts` are the only backend boundaries; the browser does not call Knowledge directly or invent lifecycle state. The final chat UX is intentionally deferred to the web workstream.

Run `pnpm install`, `pnpm typecheck`, `pnpm test` and `pnpm build` from this directory. Use `pnpm dev` for the placeholder page.
