# Knowledge service scaffold

This Python 3.12/FastAPI runtime owns evidence production: understanding, retrieval, answerability, drafting, verification, ingestion and quality analytics. The deterministic endpoints are contract-shaped stubs until the corresponding workstream is implemented.

The service never emits `.NET` business decisions or reads support-core tables. `knowledge-worker` is a separate process entrypoint for asynchronous jobs.

Run `uv sync --extra test`, then `uv run pytest`, from this directory. Apply the knowledge-owned baseline with `DATABASE_URL=postgresql+asyncpg://... uv run alembic upgrade head`. Start the API with `uv run uvicorn tenderhack_knowledge.main:app --reload`; start the worker with `uv run tenderhack-knowledge-worker`.
