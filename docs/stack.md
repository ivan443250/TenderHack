# Technology stack

## 1. Decision summary

Two backend runtimes with one PostgreSQL, per `adr/0001-dotnet-support-core-python-knowledge-service.md`: a .NET **support core** (`api`, `api-worker`) that owns state, orchestration and decisions, and a Python **knowledge & inference service** (`knowledge`, `knowledge-worker`) that owns ingestion, retrieval, models and quality analytics.

| Area | Decision |
|---|---|
| Web | React + TypeScript + Vite |
| Node runtime | Node.js 24 LTS |
| JS package manager | pnpm |
| Support core (`api`, `api-worker`) | C# / .NET 10 LTS, ASP.NET Core Minimal API, EF Core 10 + Npgsql |
| Knowledge & inference (`knowledge`, `knowledge-worker`) | Python 3.12 + FastAPI + Pydantic v2 |
| Python package manager | uv |
| Persistence | PostgreSQL 16; EF Core migrations for `api` tables, Alembic + SQLAlchemy 2 + asyncpg for `knowledge` tables |
| Retrieval | PostgreSQL FTS + pg_trgm + pgvector (used by `knowledge` only) |
| Inter-service contract | FastAPI OpenAPI → NSwag-generated C# client; contract `v0` frozen in first hour |
| PDF parsing | pdfplumber baseline; Docling/OCR only for measured failures |
| Embeddings | Qwen3-Embedding-0.6B, up to 1024 dimensions |
| Reranking | BAAI/bge-reranker-v2-m3 |
| Generation | Qwen3-4B-Instruct-2507 |
| Inference | vLLM after smoke-test; one llama.cpp fallback if needed |
| Background jobs | one worker per runtime, PostgreSQL-backed job tables/outbox |
| Deployment | Docker Compose on Linux, local volumes, reverse proxy if needed |

These model choices are starting hypotheses. Hardware and quality gates may replace a model/runtime, but not silently.

## 2. Why two runtimes

The previous reviewed spec chose a single Python modular monolith to avoid duplicated contracts, network failure modes and two migration systems. That reasoning is still valid; the split is accepted because the team's backend/integration engineer is materially faster in C#, and the `execution-plan.md §2` role split (backend vs ML/data) already draws the same line. The cost is paid consciously and mitigated by the rules in ADR-0001 §3:

- one orchestrator, in .NET; Python returns facts/scores, never decisions;
- contract `v0` frozen early, stubs first, C# client generated, not hand-written;
- strict table ownership; no cross-runtime reads at all — analytics facts are pushed `api → knowledge` as payloads;
- `knowledge` unavailability is its own failure category.

Neither runtime is a general-purpose «backend» for the other: `api` never touches models, embeddings, PDF parsing or `kb_*` tables; `knowledge` never touches `cases`, `handoffs`, `Decision` or the browser.

## 3. .NET baseline (`src/support-core`)

```text
.NET 10 LTS SDK
ASP.NET Core Minimal API
EF Core 10 + Npgsql.EntityFrameworkCore.PostgreSQL
Microsoft.Extensions.Hosting (BackgroundService) for api-worker
NSwag (client generation from knowledge OpenAPI, build-time or checked-in generated file)
System.Text.Json (source-generated where cheap)
Microsoft.Extensions.Http.Resilience (per-stage timeouts/retries towards knowledge)
```

Quality tooling:

```text
dotnet format          formatting
Roslyn analyzers       TreatWarningsAsErrors in Directory.Build.props
xUnit + FluentAssertions (or plain Assert — pick one and pin it)
Testcontainers.PostgreSql for Infrastructure/Api tests
```

Not in `api`: MediatR, AutoMapper, generic repository/UoW frameworks, `Result<T>` libraries, ONNX Runtime, tokenizers, pgvector mapping, PDF parsers. If a use-case needs a handler, it is a class with one method; if a mapping is needed, it is a static method.

## 4. Python baseline (`src/knowledge`)

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

## 5. Frontend baseline

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

Frontend talks only to `api`.

## 6. PostgreSQL as primary infrastructure

PostgreSQL 16 is chosen because the MVP needs:

- transactional case state;
- outbox/idempotency;
- relational metadata/provenance;
- full-text search;
- trigram matching;
- vector retrieval.

Extensions (created by the PostgreSQL admin bootstrap before runtime migrations):

```sql
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
```

Two DB roles and schemas: `api_rw` owns the `api` schema; `knowledge_rw` owns the `knowledge` schema. Neither role can read the other's tables; there are no shared views. Table ownership: `architecture.md §8`.

### Why no separate vector DB initially

The reviewed corpus is thousands, not millions, of semantic fragments. Exact vector search is cheap enough initially and easier to reason about.

A 1024-d float32 vector is about 4 KiB before overhead; 10k vectors are still small enough that operational simplicity dominates.

### Why no HNSW initially

ANN adds tuning and recall/filter tradeoffs. Enable only after benchmark proves exact vector search is a latency bottleneck.

## 7. Retrieval implementation

PostgreSQL FTS is **not called BM25** in docs/pitch unless BM25 is explicitly implemented.

Initial components (all inside `knowledge`):

- exact code/status/entity search;
- `to_tsvector` / `tsquery` + `ts_rank`/`ts_rank_cd`;
- `pg_trgm` for spelling/term expansion;
- pgvector exact similarity;
- application-layer RRF;
- local reranker.

Keep retrieval behind a typed interface so production can move to OpenSearch later without rewriting product logic.

## 8. Parsing stack

### Baseline: pdfplumber

Use for text PDFs and many tables because it is light and easy to inspect.

### Docling

Use only when its structured/table output improves specific problematic pages. It is not mandatory infrastructure.

### OCR

Address individual scanned/visual pages only after detecting extraction loss. Text density alone does not prove that a page is correctly parsed.

Store page/section/anchor provenance for every fragment.

## 9. Local model stack

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

## 10. Inference runtime

### Primary: vLLM

Use if actual GPU/runtime smoke test passes.

Benefits:

- local server boundary;
- batching/concurrency;
- structured outputs support;
- predictable API boundary.

### Fallback: llama.cpp

Only if vLLM/model does not fit/boot reliably on provided hardware.

Do **not** maintain two inference paths in parallel. Hardware gate chooses one. Only `knowledge` talks to the inference runtime.

## 11. Hardware gate

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

## 12. Context budget

Initial project limit (config in `knowledge`):

- max request context: ~8192 tokens;
- sources: up to ~6000;
- output: up to ~800;
- reranker pair limit separately enforced.

Never silently truncate the part of a procedure containing an exception/forbidden condition.

## 13. Background jobs

One worker per runtime, using the same application code as its API:

- `api-worker`: `outbox` table — handoff delivery/retries, `handoff-status-sync` polling, quality-turn and feedback payload pushes (`POST /v0/quality/turns`, `POST /v0/quality/feedback`), stale-turn cleanup;
- `knowledge-worker`: `knowledge_jobs` table — ingestion, embeddings, quality audit, issue groups.

`knowledge-worker` mechanism:

- job table;
- `FOR UPDATE SKIP LOCKED` claiming;
- lease/heartbeat;
- retries with explicit attempt/error state;
- idempotent effect key.

`api-worker` mechanism (single replica, hackathon scale — no lease table needed):

- independent fixed-interval `BackgroundService` ticks, one per concern;
- each tick re-derives its own work set from `cases`/`outbox` state rather than claiming rows;
- idempotent effect key per write path instead of a lease (outbox delivery keyed by handoff id, status ingestion deduped by external revision, `xmin` optimistic concurrency on `cases`);
- explicit attempt/error state on `outbox` (`AttemptCount`, `LastError`); handoff-status-sync and stale-turn cleanup log-and-retry-next-tick on failure.

Do not add Redis/Celery/Kafka/RabbitMQ/Hangfire/Quartz before a measured need; likewise, do not add an `api_jobs` lease table to `api-worker` before it needs more than one replica.

## 14. Docker/deployment

Target final development/demo runtime:

```text
web
api                 .NET, public
api-worker          .NET
knowledge           Python, internal network only
knowledge-worker    Python
postgres
inference
reverse-proxy (optional)
```

Multi-stage Dockerfiles: `sdk:10.0` → `aspnet:10.0` for .NET; the Python image installs the `src/knowledge` package from project metadata (local development uses `uv` and `uv.lock`). `api` has `depends_on` with healthcheck on `postgres` and `knowledge`; `knowledge` depends on `postgres`. The `inference` service is profile-gated and is started explicitly after local model artifacts pass a smoke test.

One `docker compose up --build` (or documented equivalent where models are pre-mounted) should start the controllable environment.

Model weights and organizer data live outside git and are mounted.

## 15. Dependency principles

Agents must apply these before adding any package (NuGet or PyPI):

1. What concrete problem does it solve now?
2. Is the same capability already in the standard stack?
3. Does it add a service/runtime/daemon?
4. Can future agents inspect/debug it locally?
5. What is its license/data behavior?
6. What test proves the dependency is useful?

Prefer boring, inspectable dependencies over opaque frameworks.

## 16. Explicit rejected choices for P0

### Single Python modular monolith (previous decision)

Superseded by ADR-0001. Remains the documented fallback if the boundary rules fail (ADR-0001 §6, §8).

### Multi-layer .NET scaffold from commit `1b57aa1`

Not restored. Its domain model (`Ticket`, `Specialist`, operator queue, threshold-based escalation) predates the reviewed spec and conflicts with the orthogonal state model; its `IThresholdProvider`/`IDateTimeProvider`/`Result<T>` layering is the ceremonial style `architecture.md §4` forbids. New `src/support-core` is written from the current `architecture.md`, not from that scaffold.

### Separate ML microservice with its own orchestration

`knowledge` is a knowledge/inference service, not an orchestrator. It does not decide `Decision`, does not own case state and does not call `api`.

### ML inference inside .NET (ONNX Runtime, ML.NET, tokenizers, pgvector mapping)

Rejected: duplicates the Python model stack and moves the volatile part into the runtime the ML engineer does not own.

### Qdrant

Technically valid, unnecessary operational component at current corpus scale.

### OpenSearch

Good future production adapter and compatible with the Portal developer ecosystem; too heavy unless current retrieval requirements justify it.

### Redis/Celery/Hangfire/Quartz

Not required for initial jobs/outbox. Re-evaluate only if PostgreSQL jobs create measured contention/latency.

### Kafka/RabbitMQ

Production integration possibilities, not hackathon baseline.

### Kubernetes

Not MVP deployment. Docker Compose is enough for reproducibility.

### MediatR / AutoMapper / generic repository frameworks

Not needed for a handful of use-cases; they hide the state machine behind indirection.

### GraphRAG / knowledge graph

No demonstrated requirement. Procedure cards + structured metadata solve current high-risk condition branches more directly.

### Generative fine-tuning

No. Retrieval/knowledge quality, answerability and evaluation are higher leverage.

### Agent framework as core runtime

No framework should obscure the explicit state machine. If a lightweight library is introduced, domain states/decisions remain owned by our C# code.

## 17. Reconsideration rule

A rejected technology can be introduced only with:

- a measured limitation of current stack;
- expected benefit;
- operational cost;
- migration/fallback path;
- targeted benchmark;
- an ADR in `docs/adr/` plus update to this document and architecture docs;
- skeptic review.
