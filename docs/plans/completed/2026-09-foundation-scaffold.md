# Foundation scaffold

Status: **COMPLETED / HISTORICAL PLAN**.

This plan records the repository-foundation phase that created the initial runnable multi-runtime scaffold. It is kept for provenance only; it is **not** the current implementation plan and must not be used to roll back later business behavior to health-only stubs.

## Goal

Create a runnable, parallel-friendly monorepo scaffold for the documented TenderHack target architecture without implementing business behavior in that foundation change.

## Non-goals of that phase

- No real retrieval, ingestion, model loading, moderation, routing, handoff or final UI in the foundation change itself.
- No contract redesign and no cross-runtime database reads.

These were historical scope limits of the scaffold task, not current repository non-goals. Later commits implemented substantial Support Core and Knowledge behavior.

## Acceptance criteria

- `src/support-core` contains a compilable .NET 10 solution with explicit Domain → Application → Infrastructure dependency direction, health API and worker host.
- `src/knowledge` contains a Python 3.12 FastAPI app, health probes, contract-shaped deterministic fixtures, settings, persistence boundary, Alembic baseline and worker entrypoint.
- `src/web` contains a React/TypeScript/Vite shell with API and SSE boundaries.
- `compose.yaml` describes web, api, api-worker, knowledge, knowledge-worker, postgres and profile-gated inference without extra brokers/services.
- Documentation uses `src/` paths and frozen contracts remain explicit.

## Completion note

The scaffold phase is complete and subsequent implementation exists. Current truth comes from `docs/product-spec.md`, `docs/product-experience.md`, `docs/architecture.md`, contracts, active plans and actual code. Current verification commands are maintained in `docs/quality.md`; do not rely on the historical environment note from the original scaffold run.
