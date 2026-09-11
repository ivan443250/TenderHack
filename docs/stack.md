# Technology stack

## 1. Decision summary

The previous .NET + separate ML service scaffold is retired. The final reviewed implementation spec chooses a simpler local-first stack aligned with the actual hackathon constraints.

| Area | Decision |
|---|---|
| Web | React + TypeScript + Vite |
| Node runtime | Node.js 24 LTS |
| JS package manager | pnpm |
| API/orchestration | Python 3.12 + FastAPI + Pydantic v2 |
| Python package manager | uv |
| Persistence | PostgreSQL 16 + SQLAlchemy 2 + Alembic + asyncpg |
| Retrieval | PostgreSQL FTS + pg_trgm + pgvector |
| PDF parsing | pdfplumber baseline; Docling/OCR only for measured failures |
| Embeddings | Qwen3-Embedding-0.6B, up to 1024 dimensions |
| Reranking | BAAI/bge-reranker-v2-m3 |
| Generation | Qwen3-4B-Instruct-2507 |
| Inference | vLLM after smoke-test; one llama.cpp fallback if needed |
| Background jobs | worker process from same Python codebase; PostgreSQL-backed jobs/outbox initially |
| Deployment | Docker Compose on Linux, local volumes, reverse proxy if needed |

These model choices are starting hypotheses. Hardware and quality gates may replace a model/runtime, but not silently.

## 2. Why Python owns the backend

The critical path is retrieval, local inference, parsing, evaluation and analytics. A separate general-purpose backend plus ML microservice adds:

- duplicated DTO/contracts;
- network failure modes;
- two migration/config systems;
- slower agent navigation;
- more deployment surface;
- no business value for the MVP.

FastAPI/Pydantic lets one codebase own HTTP boundaries and ML orchestration while preserving internal modules.

This is a **modular monolith**, not a single unstructured package.

## 3. Python baseline

Target:

```text
Python 3.12
uv
FastAPI
Pydantic v2
SQLAlchemy 2.x
Alembic
asyncpg
httpx (only for controlled internal/local adapters where needed)
```

Quality tooling to establish with scaffold:

```text
ruff        lint + format
mypy or pyright  static type check (choose one and pin it)
pytest
pytest-asyncio
coverage.py
```

Do not install both mypy and pyright unless a measured need exists. The first scaffold task picks one and records the decision.

## 4. Frontend baseline

Target:

```text
Node.js 24 LTS
pnpm
React
TypeScript
Vite
```

Recommended small set once UI scaffold begins:

- React Router if multiple routes are real;
- TanStack Query for server state;
- Zod for boundary parsing where useful;
- accessible headless primitives/component library rather than a bespoke design system during the hackathon;
- Vitest + Testing Library;
- Playwright only for critical E2E flows after P0 works.

Do not add Redux/Zustand by default. Most application state should remain server state + local UI state; introduce a global client store only when a concrete cross-route state problem appears.

## 5. PostgreSQL as primary infrastructure

PostgreSQL 16 is chosen because the MVP needs:

- transactional case state;
- outbox/idempotency;
- relational metadata/provenance;
- full-text search;
- trigram matching;
- vector retrieval.

Extensions:

```sql
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
```

### Why no separate vector DB initially

The reviewed corpus is thousands, not millions, of semantic fragments. Exact vector search is cheap enough initially and easier to reason about.

A 1024-d float32 vector is about 4 KiB before overhead; 10k vectors are still small enough that operational simplicity dominates.

### Why no HNSW initially

ANN adds tuning and recall/filter tradeoffs. Enable only after benchmark proves exact vector search is a latency bottleneck.

## 6. Retrieval implementation

PostgreSQL FTS is **not called BM25** in docs/pitch unless BM25 is explicitly implemented.

Initial components:

- exact code/status/entity search;
- `to_tsvector` / `tsquery` + `ts_rank`/`ts_rank_cd`;
- `pg_trgm` for spelling/term expansion;
- pgvector exact similarity;
- application-layer RRF;
- local reranker.

Keep retrieval behind a typed interface so production can move to OpenSearch later without rewriting product logic.

## 7. Parsing stack

### Baseline: pdfplumber

Use for text PDFs and many tables because it is light and easy to inspect.

### Docling

Use only when its structured/table output improves specific problematic pages. It is not mandatory infrastructure.

### OCR

Address individual scanned/visual pages only after detecting extraction loss. Text density alone does not prove that a page is correctly parsed.

Store page/section/anchor provenance for every fragment.

## 8. Local model stack

### Embeddings: Qwen3-Embedding-0.6B

Reasons:

- multilingual;
- instruction-aware embeddings;
- local inference;
- up to 1024 dims;
- model size reasonable for hackathon hardware.

Benchmark on real retrieval gold set before declaring it final.

### Reranker: BAAI/bge-reranker-v2-m3

Use pairwise query-passage reranking for the fused candidate set.

Its score is relevance evidence, **not a calibrated probability that the final answer is correct**.

### Generator: Qwen3-4B-Instruct-2507

Chosen starting point from the final reviewed spec because it is small enough for local serving while supporting structured answer drafting.

Do not advertise Russian superiority or quality numbers before measurement.

## 9. Inference runtime

### Primary: vLLM

Use if actual GPU/runtime smoke test passes.

Benefits:

- local server boundary;
- batching/concurrency;
- structured outputs support;
- predictable API boundary.

### Fallback: llama.cpp

Only if vLLM/model does not fit/boot reliably on provided hardware.

Do **not** maintain two inference paths in parallel. Hardware gate chooses one.

## 10. Hardware gate

In the first implementation hours, record:

- GPU model/VRAM;
- CUDA/driver;
- CPU/RAM;
- disk;
- model revision/hash.

Then run:

- 20 short requests;
- 10 long-context requests;
- 4 concurrent user requests;
- structured-output success rate;
- TTFT / total latency;
- peak memory.

Only after that pin runtime/model configuration.

## 11. Context budget

Initial project limit:

- max request context: ~8192 tokens;
- sources: up to ~6000;
- output: up to ~800;
- reranker pair limit separately enforced.

Never silently truncate the part of a procedure containing an exception/forbidden condition.

## 12. Background jobs

Start with PostgreSQL-backed jobs/outbox using the same application code.

Good initial mechanism:

- job table;
- `FOR UPDATE SKIP LOCKED` claiming;
- lease/heartbeat;
- retries with explicit attempt/error state;
- idempotent effect key.

Do not add Redis/Celery/Kafka/RabbitMQ before a measured need.

## 13. Docker/deployment

Target final development/demo runtime:

```text
web
api
worker
postgres
inference
reverse-proxy (optional)
```

One `docker compose up --build` (or documented equivalent where models are pre-mounted) should start the controllable environment.

Model weights and organizer data live outside git and are mounted.

## 14. Dependency principles

Agents must apply these before adding any package:

1. What concrete problem does it solve now?
2. Is the same capability already in the standard stack?
3. Does it add a service/runtime/daemon?
4. Can future agents inspect/debug it locally?
5. What is its license/data behavior?
6. What test proves the dependency is useful?

Prefer boring, inspectable dependencies over opaque frameworks.

## 15. Explicit rejected choices for P0

### ASP.NET Core / .NET multi-layer scaffold

Rejected for MVP because it duplicates Python orchestration/ML boundaries and was created before the final reviewed spec.

### Separate ML microservice

Rejected for same reason. Local inference may have its own process/server, but business orchestration remains in FastAPI application code.

### Qdrant

Technically valid, unnecessary operational component at current corpus scale.

### OpenSearch

Good future production adapter and compatible with the Portal developer ecosystem; too heavy unless current retrieval requirements justify it.

### Redis/Celery

Not required for initial jobs/outbox. Re-evaluate only if PostgreSQL jobs create measured contention/latency.

### Kafka/RabbitMQ

Production integration possibilities, not hackathon baseline.

### Kubernetes

Not MVP deployment. Docker Compose is enough for reproducibility.

### GraphRAG / knowledge graph

No demonstrated requirement. Procedure cards + structured metadata solve current high-risk condition branches more directly.

### Generative fine-tuning

No. Retrieval/knowledge quality, answerability and evaluation are higher leverage.

### Agent framework as core runtime

No framework should obscure the explicit state machine. If a lightweight library is introduced, domain states/decisions remain owned by our code.

## 16. Reconsideration rule

A rejected technology can be introduced only with:

- a measured limitation of current stack;
- expected benefit;
- operational cost;
- migration/fallback path;
- targeted benchmark;
- update to this document and architecture docs;
- skeptic review.
