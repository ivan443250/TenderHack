# Knowledge service

This Python 3.12/FastAPI runtime owns evidence production: understanding, normative retrieval, answerability assessment, grounded drafting, verification, ingestion and read-only quality analytics. It never emits `.NET` business decisions and never reads support-core tables.

The repository contains both real knowledge/evidence implementations and deterministic fixture/stub paths used for contract and decision tests. **Stub mode is test infrastructure, not the current product architecture or a user-facing fallback.** Current model/runtime certification status is tracked in `../../docs/adr/0003-model-stack-v2.md` and `../../docs/plans/active/model-stack-v2-real-certification.md`.

`knowledge-worker` is a separate process entrypoint for ingestion, embedding/backfill and asynchronous quality work over knowledge-owned tables.

Run `uv sync --extra test`, then `uv run pytest`, from this directory. Apply knowledge-owned migrations with `DATABASE_URL=postgresql+asyncpg://... uv run alembic upgrade head`. Start the API with `uv run uvicorn tenderhack_knowledge.main:app --reload`; start the worker with `uv run tenderhack-knowledge-worker`.
