# Foundation scaffold

## Goal

Create a runnable, parallel-friendly monorepo scaffold for the documented TenderHack target architecture without implementing business behavior.

## Non-goals

- No real retrieval, ingestion, model loading, moderation, routing, handoff or final UI.
- No contract redesign and no cross-runtime database reads.

## Acceptance criteria

- `src/support-core` contains a compilable .NET 10 solution with explicit Domain → Application → Infrastructure dependency direction, health API and worker host.
- `src/knowledge` contains a Python 3.12 FastAPI app, health probes, contract-shaped deterministic stubs, settings, persistence boundary, Alembic baseline and worker entrypoint.
- `src/web` contains a React/TypeScript/Vite shell with API and SSE boundaries only.
- `compose.yaml` describes web, api, api-worker, knowledge, knowledge-worker, postgres and profile-gated inference without extra brokers/services.
- Active documentation uses `src/` paths and frozen contracts remain unchanged.

## Verification

Run the commands recorded in `docs/quality.md` when the corresponding toolchains are available. The current environment has .NET 10, uv and Docker Compose; local Node.js is not installed, so Web verification is performed through the `node:24-alpine` container in the final report.
