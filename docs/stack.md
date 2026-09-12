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
| Retrieval | **Production:** exact + PostgreSQL FTS + pg_trgm; **experimental:** Giga embeddings + pgvector |
| Inter-service contract | frozen internal HTTP `v0`; FastAPI/OpenAPI boundary → C# client/mapping in Infrastructure |
| PDF parsing | pdfplumber baseline; Docling/OCR only for measured failures |
| Embeddings | ai-sage/Giga-Embeddings-instruct-3B-0826; 2048 normalized dimensions |
| Reranking | Querit/Querit-4B; custom adapter, quantized/runtime path gated until certified |
| Generation | empero-ai/Qwen3.8-4B-Distill, Qwen3.8-4B-Q6_K.gguf |
| Inference | recent llama.cpp provider family; embedding/generator endpoints/profiles are internal |
| Background jobs | one worker per runtime, PostgreSQL-backed job tables/outbox |
| Deployment | Docker Compose on Linux, local volumes, reverse proxy if needed |

Model choices above are **accepted selections** (ADR-0003), not completed certification. Hardware/runtime/quality gates may change an artifact/runtime only through the documented decision process; selection alone is not a benchmark result.

## 2. Why two runtimes

The previous reviewed spec chose a single Python modular monolith to avoid duplicated contracts, network failure modes and two migration systems. That reasoning is still valid; the split is accepted because the team's backend/integration engineer is materially faster in C#, and the `execution-plan.md §2` role split draws the same line. The cost is paid consciously and mitigated by ADR-0001:

- one orchestrator, in .NET; Python returns facts/scores, never decisions;
- frozen contract `v0`, generated/mapped boundary client, not hand-written domain coupling;
- strict table ownership; no cross-runtime reads at all — analytics facts are pushed `api → knowledge` as payloads;
- `knowledge` unavailability is its own failure category.

Neither runtime is a general-purpose «backend» for the other: `api` never touches models, embeddings, PDF parsing or `kb_*` tables; `knowledge` never touches `cases`, `handoffs`, `Decision` or the browser.

## 3. .NET baseline (`src/support-core`)

```text
.NET 10 LTS SDK
ASP.NET Core Minimal API
EF Core 10 + Npgsql.EntityFrameworkCore.PostgreSQL
Microsoft.Extensions.Hosting (BackgroundService) for api-worker
NSwag/generated boundary client where the contract generation path is used
System.Text.Json
Microsoft.Extensions.Http.Resilience where configured
```

Quality tooling:

```text
dotnet build / dotnet test
Roslyn analyzers / TreatWarningsAsErrors in Directory.Build.props
xUnit
```

Not in `api`: MediatR, AutoMapper, generic repository/UoW frameworks, generic `Result<T>` libraries, ONNX Runtime, tokenizers, pgvector mapping, PDF parsers. Add an abstraction/package only for a real invariant/replacement point.

## 4. Python baseline (`src/knowledge`)

Current baseline:

```text
Python 3.12
uv
FastAPI
Pydantic v2
SQLAlchemy 2.x
Alembic
asyncpg
httpx
pytest / pytest-asyncio in the test extra
```

Do not document mypy/pyright/ruff/coverage as mandatory gates until the corresponding dependency/config actually exists. If introduced, choose/pin only what is needed and add the real command to `quality.md` in the same change.

## 5. Frontend baseline

Current baseline:

```text
Node.js 24 LTS
pnpm
React
TypeScript
Vite
```

Recommended small set only as real UI needs appear:

- React Router if multiple routes are real;
- TanStack Query for server state;
- Zod for boundary parsing where useful;
- accessible headless/component primitives instead of a bespoke design system during the hackathon;
- Vitest + Testing Library;
- Playwright only for critical E2E flows after P0 works.

Do not add Redux/Zustand by default. Most application state should remain server state + local UI state. Frontend talks only to `api`.

Current Web is still a minimal shell; target UX in `product-spec.md` / `product-experience.md` is not an implementation claim.

## 6. PostgreSQL as primary infrastructure

PostgreSQL 16 is chosen because the MVP needs:

- transactional case state;
- outbox/idempotency;
- relational metadata/provenance;
- full-text search;
- trigram matching;
- vector retrieval.

Extensions (created by PostgreSQL admin bootstrap before runtime migrations):

```sql
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
```

Two DB roles and schemas: `api_rw` owns the `api` schema; `knowledge_rw` owns the `knowledge` schema. Neither role can read the other's tables; there are no shared views.

### Why no separate vector DB initially

The reviewed corpus is thousands, not millions, of semantic fragments. Exact vector search is cheap enough initially and easier to reason about.

A 2048-d float32 vector is 8192 bytes (8 KiB) before overhead; 10k vectors are about 78.1 MiB of raw values, still small enough that operational simplicity dominates.

### Why no HNSW initially

ANN adds tuning and recall/filter tradeoffs. Enable only after benchmark proves exact vector search is a latency bottleneck.

## 7. Retrieval implementation

PostgreSQL FTS is **not called BM25** in docs/pitch unless BM25 is explicitly implemented.

Production components inside `knowledge`:

- exact code/status/entity search;
- `to_tsvector` / `tsquery` + `ts_rank`/`ts_rank_cd`;
- `pg_trgm` for spelling/term expansion;
- deterministic lexical ranking (`lexical-v1`).

Giga embeddings, pgvector exact similarity, application-layer RRF and the
optional local reranker remain **experimental/benchmark-only**. AI-2/AI-2B
ablation on the frozen corpus did not provide incremental recall and the dense
layer is excluded from the online critical path; this is a measured runtime
decision, not a claim that embeddings are universally ineffective.

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

### Embeddings: Giga Embeddings 3B 0826

`ai-sage/Giga-Embeddings-instruct-3B-0826` provides Russian/English, instruction-aware, mean-pooled and L2-normalized 2048-dimensional vectors. Queries use the versioned `giga_portal_support_v1` instruction; documents are embedded as plain text. The selected `ai-babai/giga-embeddings-0826-3b-gguf` Q8_0 file is an independent third-party conversion, not an official ai-sage release. Upstream MTEB values are not TenderHack measurements.

### Reranker: Querit/Querit-4B

Use the custom Querit `AutoModel` score interface only for an explicit
benchmark after a trusted runtime/artifact/scoring path is certified. No
production quantized artifact is treated as verified merely because a filename
exists. The production retrieval path is exact/FTS/trigram; Giga dense + RRF
and Querit remain experimental. Scores are relevance evidence, **not a
calibrated probability**.

### Generator: Qwen3.8-4B-Distill

`Qwen3.8-4B-Q6_K.gguf` is the selected local generation artifact. The model may emit hidden reasoning; reasoning spans are removed/fail-closed before structured parsing and never enter evidence, API payloads or analytics.

## 10. Inference runtime

### Local llama.cpp providers

The accepted runtime family is recent llama.cpp. Embedding and generator providers/endpoints are configured independently and may be exposed behind an internal gateway/profile; Querit remains disabled until its provider/artifact/scoring interface is certified. No external AI API is used.

The repository now includes portable NVIDIA runtime packaging in addition to cloud/bundled inference assets. A Docker image/profile existing in git is not certification: readiness/latency/memory/offline evidence comes from the active Model Stack v2 certification plan.

## 11. Hardware gate

For each certified profile record:

- GPU model/VRAM;
- CUDA/driver/runtime;
- CPU/RAM/disk;
- exact model revision/hash;
- representative short/long/concurrent requests;
- structured-output success;
- TTFT / total latency where applicable;
- peak memory/VRAM;
- offline restart from local artifacts.

Only after that pin/claim a runtime configuration. Detailed runbook: `plans/active/model-stack-v2-real-certification.md`.

## 12. Context budget

Initial project limit (config in `knowledge`):

- max request context: ~8192 tokens;
- sources: up to ~6000;
- output: up to ~800;
- reranker pair limit separately enforced.

Never silently truncate the part of a procedure containing an exception/forbidden condition.

## 13. Background jobs

One worker per runtime, using the same application code as its API:

- `api-worker`: outbox — handoff delivery/retries, handoff-status-sync polling, quality-turn / feedback / completion payload pushes to Knowledge, stale-turn cleanup/recovery according to implemented policy;
- `knowledge-worker`: `knowledge_jobs` — ingestion, embeddings/backfill, quality audit, issue groups.

`knowledge-worker` mechanism may use PostgreSQL job claiming/lease/heartbeat/retries. `api-worker` intentionally stays simpler at hackathon single-replica scale; do not add Redis/Celery/Kafka/RabbitMQ/Hangfire/Quartz or an `api_jobs` lease system before a measured need.

## 14. Docker/deployment

Current development/demo topology:

```text
web
api                 .NET, public
api-worker          .NET
knowledge           Python, internal network only
knowledge-worker    Python
postgres
inference profile/provider(s)   internal, profile-gated
reverse-proxy       optional
```

Multi-stage Dockerfiles/package installs must stay reproducible. `api` depends on PostgreSQL/Knowledge according to health semantics; Knowledge depends on its own DB and only on enabled/certified model capabilities. Model weights and organizer data live outside normal git/source flow and are mounted/verified according to the repository artifact policy.

One `docker compose up --build` (or documented profile-aware equivalent where models are pre-mounted) should start the controllable environment. Presence of a profile is not a readiness claim.

## 15. Dependency principles

Agents must apply these before adding any package (NuGet/PyPI/npm) or service:

1. What concrete problem does it solve now?
2. Is the same capability already in the standard stack?
3. Does it add a service/runtime/daemon?
4. Can future agents inspect/debug it locally?
5. What is its license/data behavior?
6. What test proves the dependency is useful?

Prefer boring, inspectable dependencies over opaque frameworks.

## 16. Explicit rejected choices for P0

### Single Python modular monolith (previous decision)

Superseded by ADR-0001. Remains historical fallback context if ADR revisit conditions are met.

### Multi-layer .NET scaffold from commit `1b57aa1`

Not restored. Its old `Ticket`/`Specialist`/operator-queue model predates the reviewed state model and conflicts with current ownership/invariants. Current Support Core is built from `architecture.md`, not that scaffold.

### Separate ML microservice with its own orchestration

`knowledge` is knowledge/inference, not an orchestrator. It does not decide `Decision`, own case state or call `api`.

### ML inference inside .NET

Rejected: duplicates the Python model stack and moves volatile inference into the wrong ownership boundary.

### Qdrant

Technically valid, unnecessary operational component at current corpus scale.

### OpenSearch

Good future production adapter and compatible with the Portal developer ecosystem; too heavy unless current retrieval requirements justify it.

### Redis/Celery/Hangfire/Quartz

Not required for initial jobs/outbox. Re-evaluate only on measured contention/latency/replica need.

### Kafka/RabbitMQ

Production integration possibilities, not hackathon baseline.

### Kubernetes

Not MVP deployment. Docker Compose is enough for reproducibility.

### MediatR / AutoMapper / generic repository frameworks

Not needed for current use-cases; avoid hiding the state machine behind indirection.

### GraphRAG / knowledge graph

No demonstrated requirement. Procedure cards + structured metadata solve current condition branches more directly.

### Generative fine-tuning

No. Retrieval/knowledge quality, answerability and evaluation are higher leverage.

### Agent framework as core runtime

No framework should obscure the explicit state machine. Product decisions remain owned by C# code.

## 17. Reconsideration rule

A rejected/deferred technology can be introduced only with:

- a measured limitation of current stack or explicit new requirement;
- expected benefit;
- operational cost;
- migration/fallback path;
- targeted benchmark/test;
- ADR when runtime/service/DB/state/model policy changes;
- synchronized stack/architecture docs;
- skeptic review.
