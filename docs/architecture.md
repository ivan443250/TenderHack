# Architecture

## 1. Architectural goal

MVP must be easy to reason about, easy to run on one Linux host, locally inferenced, and explicit about failure states. The architecture optimizes for **correctness, observability and agent legibility**, not service count.

Chosen shape (see `adr/0001-dotnet-support-core-python-knowledge-service.md`):

- **`api`** — .NET support core: HTTP/SSE boundary, case state machine, orchestrator, decisions, moderation rules, routing, handoff, feedback. Clean-architecture layering without ceremonial layers.
- **`api-worker`** — same .NET codebase, background process: outbox delivery, handoff retries, quality-turn and feedback pushes to `knowledge`.
- **`knowledge`** — Python knowledge & inference service: ingestion, retrieval, answerability assessment, draft/verify, quality analytics. Internal-only HTTP.
- **`knowledge-worker`** — same Python codebase: ingestion, embeddings, quality audit, issue-group recomputation.
- **`web`** — React SPA.
- **PostgreSQL 16** — single database, strict per-runtime table ownership.
- **inference** — local vLLM (or documented fallback) used only by `knowledge`.

Do not add a third backend runtime, a queue or a separate vector DB unless new hard requirements justify the split.

## 2. Target repository layout

```text
src/
  support-core/                 .NET solution (support core)
    TenderHack.sln
    src/
      TenderHack.Domain/        cases, turns, handoff, enums, transitions, policies — no framework refs
      TenderHack.Application/   use-cases, TurnOrchestrator, ports (interfaces), own records
      TenderHack.Infrastructure/ EF Core + Npgsql, outbox/jobs, generated knowledge client, adapters
      TenderHack.Api/           Minimal API, SSE, ProblemDetails, authz, demo mode
      TenderHack.Worker/        BackgroundService host for outbox/handoff/quality triggers
    tests/
      TenderHack.Domain.Tests/
      TenderHack.Application.Tests/
      TenderHack.Contract.Tests/ frozen-contract artifact checks
  knowledge/                    Python service (knowledge & inference)
    pyproject.toml
    src/tenderhack_knowledge/
      api/                      FastAPI routes (internal contract v0)
      contracts/                typed v0 boundary models
      understanding/            query understanding boundary
      retrieval/                retrieval boundary
      knowledge/                corpus and provenance boundary
      answerability/            evidence sufficiency boundary
      inference/                narrow model adapter protocols
      verification/             claim/source verification boundary
      ingestion/                source ingestion boundary
      quality/                  evaluator and issue groups
      persistence/              knowledge-owned DB session boundary
      observability/            structured logging
      settings/                 environment settings
      worker/                   thin entrypoint
    migrations/                 Alembic baseline for knowledge-owned jobs
    tests/                      health and contract-stub smoke tests
  web/
    package.json
    src/
evals/                          evaluation workspace (data is not committed yet)
tests/e2e/                      cross-runtime smoke scenarios
scripts/
docs/
```

If a simpler physical layout improves speed, keep these **logical boundaries** even if directories differ. Update this document if that happens.

## 3. System context

```text
Browser
   │ HTTP/SSE
   ▼
api (.NET)  ── single public boundary
   ├── Case orchestration / turn state machine
   ├── Decision (ANSWER / CLARIFY / HANDOFF_OFFER / ...)
   ├── Deterministic moderation rules
   ├── Routing / handoff / outbox / status ingestion
   ├── Completion / archive / notifications
   ├── Feedback
   └── Read-only analytics (proxied, authz here)
   │
   ├────────────► PostgreSQL 16 (api-owned tables)
   │               cases/messages/turns/case_events
   │               handoffs/handoff_status_updates/outbox
   │               feedback/notifications/api_jobs
   │
   └── HTTP, contract v0, X-Trace-Id ────► knowledge (Python)
                                             ├── understand / retrieve / answerability
                                             ├── draft / verify
                                             ├── moderation context check
                                             ├── sources (document/page/fragment)
                                             ├── quality-audit / feedback intake (pushed payloads)
                                             └── quality / issue groups (read models)
                                             │
                                             ├──► PostgreSQL 16 (knowledge-owned tables)
                                             │     kb_* / quality_cases / quality_evaluations
                                             │     issue_groups / knowledge_jobs
                                             │     FTS / pg_trgm / pgvector
                                             ├──► local embedding/reranker runtime
                                             └──► local generation runtime (vLLM)

api-worker (.NET)                 knowledge-worker (Python)
   ├── outbox delivery                ├── ingestion / parsing / indexing
   ├── handoff adapter retries        ├── embeddings batches
   ├── handoff-status-sync (poll) ◄── support adapter (demo / real)
   └── quality-audit / feedback ────► └── quality audit / issue groups
       push (full payload, outbox)        (works only on knowledge-owned tables)

api (.NET) ◄── POST /api/v0/integrations/support/status   optional HMAC webhook
                                                          (real adapter push only)
```

`knowledge` has **no dependency on `api`**: no HTTP calls back, no reads of `api` tables, no shared views. Everything it knows about cases arrives as pushed payloads over contract `v0`. It can be started, tested and evaluated with `api` absent.

No business decision may exist only inside frontend state, prompt prose, or the Python service.

## 4. Dependency direction

### 4.1. Inside `api` (.NET)

```text
TenderHack.Api (HTTP/SSE)
   ↓
TenderHack.Application (use-cases, TurnOrchestrator, ports)
   ↓
TenderHack.Domain (state, invariants, policies)
   ↑ implemented by
TenderHack.Infrastructure (EF Core, outbox, knowledge client, handoff adapter)
```

- `Domain` references no framework: no EF Core, no ASP.NET, no generated client types.
- `Application` depends on `Domain` and on its own port interfaces (`ICaseRepository`, `IUnitOfWork`, `IOutbox`, `IKnowledgeService`, `IHandoffAdapter`, `ITurnEventStream`). It never sees `DbContext` or NSwag-generated classes.
- `Infrastructure` parses raw/external shapes (DB rows, knowledge JSON) at its boundary and returns typed `Application` records.
- Avoid ceremonial layers that only proxy calls (`IDateTimeProvider`-style abstractions, generic `Result<T>` wrappers, repository-per-table). A boundary exists only where it protects a real invariant or replacement point. The retired scaffold from commit `1b57aa1` is the negative example.

### 4.2. Inside `knowledge` (Python)

```text
FastAPI routes
   ↓
knowledge / inference / quality modules (typed Pydantic contracts)
   ↓
persistence / model adapters
```

`knowledge` has no notion of `Decision`, `HandoffStatus` or `ResolutionStatus`.

### 4.3. Between runtimes

`api → knowledge` only, in every sense:

- HTTP: `knowledge` never calls `api`;
- database: `knowledge` never reads `api` tables (no views, no shared role, no cross-schema `SELECT`);
- data: case facts needed for analytics are **pushed** by `api-worker` (`POST /v0/quality/turns`, `POST /v0/quality/feedback`, §10) and stored in `knowledge`-owned `quality_cases`;
- code: no shared package; the only shared artifact is the OpenAPI document emitted by `knowledge`.

Consequence: `knowledge` is a standalone service with `api` as its only client. `evals/quality` runs against `knowledge` alone with fixture payloads.

## 5. Core modules

Owner column is normative (see ADR-0001 §3).

### 5.1. Cases — owner: `api`

Owns:

- case identity and `owner_id` (anonymous browser session, §17);
- conversation state, including `moderation_warning_count`;
- turn revisions;
- decision history;
- resolution status and completion (§5.8);
- handoff state, stage and specialist facts received from the adapter (§5.6, §15);
- feedback references;
- notifications derived from case events (§5.9).

Critical rule: state transitions are explicit methods with preconditions on the aggregate, not arbitrary field mutation.

### 5.2. Orchestration — owner: `api`

`TurnOrchestrator` (Application layer) owns the per-turn state machine:

```text
persist input
→ moderate (deterministic rules; knowledge.moderation_context only on ambiguity)
→ direct human-request check
→ understand context            (knowledge.understand)
→ exact rules/entities
→ retrieve knowledge            (knowledge.retrieve)
→ answerability                 (knowledge.answerability → evidence assessment; decision here)
→ generate draft if allowed     (knowledge.draft)
→ verify draft                  (knowledge.verify)
→ decide Decision + reason codes (C# policy)
→ persist final decision atomically
```

Every stage result is persisted as a `case_events` row before the next stage starts; that is what SSE streams to the browser.

Post-case quality runs asynchronously in `knowledge-worker` and must not block the user response.

### 5.3. Knowledge — owner: `knowledge`

Owns two physically/logically separated corpora:

- normative answer corpus;
- historical analytical corpus.

No shared convenience query that lets generation accidentally retrieve historical resolutions as normative evidence. `retrieve` takes an explicit `corpus` parameter and defaults to normative.

### 5.4. Moderation — owner: `api` (rules), `knowledge` (context check)

Deterministic-first in `Domain`: profanity normalizer, rule set with version, traceable match metadata. The LLM/context check lives in `knowledge` and is called only for ambiguity; it returns an ambiguity assessment, not a moderation outcome. The policy outcome is produced in `api`.

Policy is **warning-first** (`product-spec.md §14`): the first confirmed violation in a case yields `Decision = MODERATION_WARNING` and increments `moderation_warning_count`; a confirmed violation when `moderation_warning_count >= Moderation:CloseAfterWarnings` (default `1`) yields `MODERATION_CLOSE` → `ConversationStatus = CLOSED_MODERATION`. The threshold is server configuration, never a client parameter. Both outcomes end the current turn before understand/retrieve/draft run. The counter is case-scoped: a new case starts at `0`.

### 5.5. Routing — owner: `api`

Owns `service_need`, `recommended_line`, `dispatch_queue`, `engineering_review_suggested`, `reason_codes` and channel policy as `Domain` policies.

It must not pretend that inferred support-line labels from history are official ground truth.

### 5.6. Handoff — owner: `api`

Owns:

- editable prepared package;
- `PENDING / ACCEPTED / SIMULATED_ACCEPTED / FAILED`;
- adapter idempotency key;
- outbox delivery (`api-worker`);
- external ticket ID when real/simulated adapter returns one;
- **status ingestion after acceptance**: `stage`, `assigned_specialist`, terminal outcome — as adapter facts, stored in `handoff_status_updates` and projected to `HANDOFF_STATUS` case events (§15).

Handoff package must be buildable with `knowledge` down.

Status facts reach `api` through two channels that converge in one Application use-case (`IngestHandoffStatus`):

1. **polling** — `api-worker` job `handoff-status-sync` calls `IHandoffAdapter.GetStatusAsync` for every non-terminal accepted handoff (P0 mechanism; works with the demo adapter and with any real system that has a read endpoint);
2. **inbound webhook** — `POST /api/v0/integrations/support/status`, HMAC-signed, disabled unless configured (used only when a real adapter can push).

Neither channel may invent a specialist or stage; a null from the adapter stays null in the UI.

### 5.7. Quality / Analytics — owner: `knowledge`

Read-only with respect to support work. Produces evaluation/profile/hypothesis artifacts with provenance and limitations. Does not update employee ratings or publish knowledge automatically.

Input case data arrives only as pushed payloads from `api-worker` (§10: `POST /v0/quality/turns`, `POST /v0/quality/feedback`) and is stored in `knowledge`-owned `quality_cases`. `Decision`, `reason_codes`, `handoff_status` and similar values inside those payloads are opaque string labels for `knowledge` — it groups and reports on them, never interprets or reproduces the policy behind them. Historical analytical corpus is `knowledge`'s own data already.

Results are exposed to the browser through `api` proxy endpoints with server-side authorization.

### 5.8. Completion and archive — owner: `api`

«Завершение обращения» is an explicit aggregate transition, never inferred from `ANSWER` or from silence. A case completes by exactly one of:

| Trigger | `ConversationStatus` | `ResolutionStatus` |
|---|---|---|
| user command `complete` (`solved: true / false / null`) | `CLOSED_USER` | `RESOLVED` / `UNRESOLVED` / `UNKNOWN` |
| adapter terminal status (`RESOLVED`, `CLOSED_UNRESOLVED`, `CANCELLED`) via §5.6 | `CLOSED_SUPPORT` | `RESOLVED` / `UNRESOLVED` / `UNKNOWN` |
| second confirmed profanity violation (§5.4) | `CLOSED_MODERATION` | unchanged |

Completion emits, in one transaction: `CASE_RESOLUTION_CHANGED` (if changed), `CASE_COMPLETED`, `FEEDBACK_REQUESTED` (not for `CLOSED_MODERATION`), and a notification (§5.9). A completed case is read-only for new user messages; it stays fully readable and is listed by `GET /api/v0/cases?status=archived`. «Архив» is a projection over `ConversationStatus != ACTIVE`, not a fourth lifecycle enum.

There is no inactivity auto-completion in P0.

### 5.9. Notifications — owner: `api`

Notifications are derived facts, written in the same transaction as the case event that causes them, into the `api`-owned `notifications` table, scoped by `owner_id`:

| Case event | Notification type |
|---|---|
| `HANDOFF_STATUS` with a new `stage` / `assigned_specialist` / acceptance | `HANDOFF_UPDATED` |
| `CASE_COMPLETED` | `CASE_COMPLETED` |
| `FEEDBACK_REQUESTED` | `FEEDBACK_REQUESTED` |

Delivery tiers (ADR-0002):

1. **persisted inbox** — `GET /api/v0/notifications?after=` on return, badge on archive list; survives closed browser;
2. **owner-level SSE** — `GET /api/v0/notifications/stream`, independent of any one case; the browser renders a toast and, when the tab is hidden and permission was granted, an OS-level notification through the Web Notifications API (local, no external push service);
3. **Web Push / email / SMS — not P0.** Web Push depends on external browser push services and breaks the offline demo requirement (`quality.md §11`); email/SMS require contact data the product does not collect and a delivery service the stack does not include.

`MODERATION_WARNING` is rendered in the chat timeline and is not a notification.

## 6. State model

Keep orthogonal dimensions:

```text
ConversationStatus = ACTIVE | CLOSED_USER | CLOSED_SUPPORT | CLOSED_MODERATION
ResolutionStatus   = UNKNOWN | RESOLVED | UNRESOLVED
TurnStatus         = QUEUED | RUNNING | COMPLETED | FAILED | SUPERSEDED
HandoffStatus      = NOT_REQUESTED | PENDING | ACCEPTED | SIMULATED_ACCEPTED | FAILED
Decision           = ANSWER | CLARIFY | HANDOFF_OFFER | ANSWER_AND_HANDOFF |
                     MODERATION_WARNING | MODERATION_CLOSE | TECHNICAL_ERROR
```

Additional case-scoped state that is not an enum:

```text
moderation_warning_count : int      (§5.4)
handoff.stage            : { code, display_name }?        adapter fact, not a Domain enum (§15)
handoff.assigned_specialist : { ref, display_name? }?     adapter fact
handoff.terminal         : RESOLVED | CLOSED_UNRESOLVED | CANCELLED | null
handoff.stale            : bool      status polling reached TTL without a terminal fact (§12)
completion_reason        : USER | SUPPORT | MODERATION | null   (§5.8)
```

Do not collapse these into one enum. These enums are defined in `TenderHack.Domain`; their string values are the contract for `case_events`, the web API and the pushed quality payloads. `knowledge` receives them as opaque labels for analytics and never produces them.

### Invariants (must exist as `Domain` unit tests)

- One active revision is authoritative.
- Running old turn cannot publish after a newer user revision supersedes it.
- `ANSWER` cannot set `RESOLVED`.
- Positive feedback (`information_quality_rating`, `specialist_rating`) cannot set `RESOLVED`; only `complete(solved=true)` or an adapter terminal `RESOLVED` can.
- Handoff status changes to accepted only on adapter acknowledgement.
- `stage` / `assigned_specialist` / `terminal` change only through `IngestHandoffStatus` with an adapter-sourced payload; a status update cannot target a different `handoff_id`/`case_id` than it was issued for.
- Duplicate status update (same `handoff_id` + `external_revision`) is a no-op.
- Terminal adapter status on a case that is already `CLOSED_USER` updates `ResolutionStatus`/handoff facts but does not reopen or re-close the conversation.
- First confirmed profanity violation produces `MODERATION_WARNING`, not `MODERATION_CLOSE`; the close threshold is read from configuration.
- Moderation warning/close does not delete or revoke already accepted handoff.
- Failed AI turn does not silently mutate previous accepted handoff.
- A completed case rejects new user messages with `CASE_CLOSED`.
- `knowledge` failure on any stage produces `TECHNICAL_ERROR` with a category, never a knowledge-not-found decision.

## 7. Idempotency and concurrency

All state-changing public commands accept an idempotency key where duplicate client retries are plausible.

Expected behavior:

- same key + same payload → same logical result;
- same key + different payload → conflict;
- only one active/accepted handoff per case/problem;
- both workers are at-least-once; effects are idempotent by business key;
- turn publication unique by `turn_id` + revision;
- handoff status ingestion unique by `handoff_id` + `external_revision` (poll and webhook share this key, so a fact delivered by both channels is applied once);
- notification unique by `(owner_id, case_id, type, source_event_id)`;
- jobs use lease/heartbeat or equivalent recoverable claiming;
- `api → knowledge` calls are retried only for idempotent stages (understand/retrieve/answerability/verify); `draft` is retried at most once and only if no draft was persisted.

Do not claim exactly-once delivery.

## 8. Persistence

PostgreSQL 16 is both system of record and initial retrieval engine. One database, two migration systems, **strict table ownership**:

| Owner | Migration tool | Tables |
|---|---|---|
| `api` | EF Core migrations | `cases`, `messages`, `turns`, `case_events`, `handoffs`, `handoff_status_updates`, `outbox`, `feedback`, `notifications`, `api_jobs`, `idempotency_keys` |
| `knowledge` | Alembic | `kb_documents`, `kb_document_versions`, `kb_snapshots`, `kb_fragments`, `kb_condition_cards`, `kb_ingestion_runs`, `quality_cases`, `quality_evaluations`, `issue_groups`, `knowledge_jobs` |

Rules:

- a runtime never migrates, writes or directly selects from the other runtime's tables — no views, no shared roles, no cross-schema `SELECT` in either direction;
- each runtime has its own DB role (`api_rw`, `knowledge_rw`) with privileges only on its own tables; a runtime's tests may run against a database where the other runtime's tables do not exist;
- `quality_cases` is `knowledge`'s own copy of the case facts it was sent (§10); duplication of question/answer text is accepted at hackathon scale;
- `api` never selects from `kb_*`/`quality_*`; source view and analytics data come through `knowledge` HTTP;
- runtime schemas are created by the PostgreSQL admin bootstrap (`api` and `knowledge`), and each role's `search_path` is scoped to its own schema;
- the only shared database capability is `pgvector`/`pg_trgm` extension presence, installed by the PostgreSQL admin bootstrap before either runtime migration.

### Knowledge versioning

Every user-facing answer records at least (persisted by `api` from `knowledge` responses):

- snapshot ID;
- fragment IDs;
- model/retrieval config version;
- final decision/reason codes.

This makes demo/debug reproduction possible.

## 9. Retrieval architecture — owner: `knowledge`

Initial retrieval stays in PostgreSQL:

```text
exact entity/code lookup
       +
PostgreSQL FTS
       +
pgvector exact cosine/IP
       ↓
application-layer RRF
       ↓
dedupe
       ↓
local reranker
       ↓
applicability / evidence assessment
```

Do not call PostgreSQL FTS «BM25» unless BM25 is actually introduced.

No HNSW until exact retrieval latency is measured and shown to be a bottleneck on the real corpus.

## 10. Knowledge service contract (v0)

This is the only interface between runtimes. It is frozen as `v0` in the first implementation hour (`execution-plan.md §3`); `knowledge` serves stubs with plausible JSON until real models are wired. OpenAPI is emitted by FastAPI; the C# client is generated (NSwag) into `TenderHack.Infrastructure` and never leaks into `Application`.

The normative, exact version of this contract lives in [`docs/contracts/knowledge-v0.md`](contracts/knowledge-v0.md) (rules, headers, error taxonomy, idempotency, versioning) and [`docs/contracts/knowledge-v0.openapi.yaml`](contracts/knowledge-v0.openapi.yaml) (machine-readable OpenAPI 3.0, used for NSwag generation until `knowledge` emits its own OpenAPI from FastAPI — the two must then stay in lockstep). The shapes below stay illustrative; the two files above are authoritative on conflict.

Design rules:

- **facts, scores and evidence assessments in; no decisions out** — responses never contain `decision`, `should_handoff`, `handoff_status` or decision-family reason codes;
- every request carries `X-Trace-Id`, `X-Case-Id`, `X-Turn-Id`;
- every response carries `model_version` / `retrieval_config_version` where applicable;
- new fields are optional; breaking changes bump the path version and ship with both sides in one change;
- per-stage timeouts are `api` config; a timeout, 5xx, connection error or schema-invalid body maps to `KnowledgeFailure { category: TIMEOUT | UNAVAILABLE | INVALID_RESPONSE | MODEL_ERROR }`.

Endpoints (request/response shapes are illustrative; the emitted OpenAPI is authoritative):

```text
GET  /health

POST /v0/understand
     { text, prior_turn_summary? }
  →  { normalized_text, entities[], exact_codes[], language_flags }

POST /v0/moderation/context
     { text, rule_match }
  →  { ambiguity: OFFENSIVE | NOT_OFFENSIVE | UNCERTAIN, model_version }

POST /v0/retrieve
     { query, entities[], exact_codes[], corpus: NORMATIVE | HISTORICAL, snapshot_id? }
  →  { snapshot_id, retrieval_config_version,
       candidates[ { fragment_id, document_id, page, anchor,
                     scores { exact?, fts?, dense?, rerank? },
                     applicability_flags[] } ] }

POST /v0/answerability
     { query, snapshot_id, candidate_fragment_ids[] }
  →  { evidence_sufficiency: SUFFICIENT | CONDITION_DEPENDENT | INSUFFICIENT,
       evidence_fragment_ids[], missing_conditions[], risk_flags[] }

POST /v0/draft
     { query, snapshot_id, evidence_fragment_ids[], constraints }
  →  { draft_markdown, claims[ { claim_id, text, fragment_ids[] } ],
       model_version, token_usage }

POST /v0/verify
     { claims[], snapshot_id }
  →  { results[ { claim_id, supported: bool, evidence_fragment_ids[] } ] }

GET  /v0/sources/{fragment_id}
  →  { document_id, title, version, page, anchor, text, snapshot_id }

GET  /v0/snapshots/current

POST /v0/quality/turns               (pushed by api-worker via outbox; at-least-once;
     {                                 idempotent by turn_id + revision; 202 Accepted)
       case_id, turn_id, revision, occurred_at,
       question_text, prior_turns_summary?,
       decision, reason_codes[],                 -- opaque labels for knowledge
       answer_markdown?, evidence_fragment_ids[], snapshot_id?,
       handoff_status?, recommended_line?, service_need?,
       stage_timings { understand_ms?, retrieve_ms?, draft_ms?, verify_ms? },
       error_category?
     }

POST /v0/quality/feedback            (pushed by api-worker via outbox; at-least-once;
     {                                 idempotent by feedback_id)
       feedback_id, case_id, turn_id, occurred_at,
       specialist_rating?, information_quality_rating?,   -- POSITIVE | NEGATIVE
       specialist_ref?, integration_mode?,
       solved: bool?, comment_text?
     }

POST /v0/quality/completions         (pushed by api-worker via outbox; at-least-once;
     {                                 idempotent by case_id)
       case_id, completed_at,
       completion_reason, resolution_status,             -- opaque labels for knowledge
       handoff_status?, integration_mode?, specialist_ref?, stage_code?,
       moderation_warning_count?, turn_count?
     }

GET  /v0/quality/evaluations?case_id=
GET  /v0/quality/issue-groups
```

Push rules:

- `api-worker` sends the full fact set; `knowledge` never asks `api` for more and never reads `api` tables to fill gaps;
- a field `knowledge` needs but does not receive is added as an optional field to the payload, not fetched from the database;
- payload is emitted through the `api` outbox after the turn is committed, so a crash between commit and push is recovered by redelivery;
- `knowledge` stores the payload in `quality_cases` as received and runs evaluation asynchronously in `knowledge-worker`; `202` means «stored», not «evaluated».

The mapping from `evidence_sufficiency` + `verify.results` + moderation/routing outcomes to `Decision` is C# code in `TenderHack.Domain`/`Application` and is covered by `evals/decisions`.

## 11. Local inference boundary — owner: `knowledge`

Inside `knowledge` the application sees narrow typed adapters:

```text
Embedder.embed(texts)
Reranker.score(query, passages)
Generator.generate_structured(request_schema)
Verifier.check_claim(claim, evidence)
```

No module gets a generic «LLM with arbitrary tools» interface.

The model cannot call SQL, shell or arbitrary HTTP. `api` never talks to vLLM.

### Generator budget

Default design target from reviewed spec:

- ordinary turn: ≤ 2 generative calls;
- complex verification path: ≤ 3;
- input context budget initially 8192 tokens;
- no silent truncation of conditions/exceptions.

These are `knowledge` config values and must be benchmarked on hardware.

## 12. Background workers

Two worker processes, each built from its runtime's codebase, separate only because work duration/lifecycle differs.

| Worker | Jobs | Job table |
|---|---|---|
| `api-worker` (.NET `BackgroundService`) | outbox delivery, handoff adapter submit retries, `handoff-status-sync` polling of non-terminal accepted handoffs (backoff from `Support:StatusPoll:Initial` to `Support:StatusPoll:Max`, stops at terminal status or `Support:StatusPoll:Ttl`), quality-turn / feedback payload pushes to `knowledge`, stale-turn cleanup | `api_jobs`, `outbox` |
| `knowledge-worker` (Python) | ingest/parse/index, embeddings batches, quality audit, issue-group recomputation | `knowledge_jobs` |

Both start with PostgreSQL job tables / `FOR UPDATE SKIP LOCKED`, lease/heartbeat, explicit attempt/error state. Do not add Redis/Kafka until measured need.

## 13. Frontend boundaries

Primary surfaces:

1. **Case / chat** — question, progress, answer with source buttons, clarification, moderation warning, handoff offer → editable package → status widget (status, `integration_mode`, stage/specialist when supplied), completion notice, feedback widget.
2. **Source view** — exact document/page/fragment (served by `api` via `knowledge /v0/sources`).
3. **Case list / archive** — owner-scoped list of active and completed cases (`GET /api/v0/cases`), completed cases open read-only.
4. **Notifications** — inbox badge + toast from the owner-level SSE stream; OS-level notification via Web Notifications API when the tab is hidden (§5.9).
5. **Read-only quality/issues** — quality evidence and repeated problem groups (served by `api` proxy).

Technical trace for judges/team is protected detail, not end-user UI.

Frontend talks only to `api`. It never invents lifecycle state. It renders server state and can reconnect using monotonic event IDs / polling fallback.

## 14. Progress transparency

Show semantic stages, not fake percentages:

```text
Проверяем запрос
Ищем применимые инструкции
Проверяем условия
Готовим ответ / передачу
```

Stages are `case_events` written by `TurnOrchestrator`. Technical detail may expose event types and reason codes, but not chain-of-thought.

## 15. Handoff adapter — owner: `api`

P0 adapter is controlled by the team and explicitly demo-labelled. Normative semantics: `contracts/support-adapter-v0.md`.

Interface concept:

```csharp
public interface IHandoffAdapter
{
    // outbound: submit prepared package, get synchronous acknowledgement
    Task<HandoffAck> SubmitAsync(HandoffRequest request, string idempotencyKey, CancellationToken ct);

    // inbound (pull): current external status; null when the adapter has no status API
    Task<HandoffStatusSnapshot?> GetStatusAsync(HandoffStatusQuery query, CancellationToken ct);
}
```

`HandoffStatusSnapshot` carries only adapter facts: `external_case_id`, `stage { code, display_name }?`, `assigned_specialist { ref, display_name? }?`, `terminal?`, `external_revision`, `occurred_at`, `integration_mode`. The same record is produced by the optional inbound webhook (`POST /api/v0/integrations/support/status`), so `IngestHandoffStatus` is the single write path regardless of channel.

Demo adapter modes (selected by config, never by request payload): submit `success | timeout | failure`; status timeline `none | staged` — `staged` replays a deterministic script per `handoff_id` (queued → assigned to «Демо-специалист» → in progress → resolved) with configurable delays. Every simulated fact carries `integration_mode = SIMULATED` and the UI labels it as demo.

Real Portal integration remains a future adapter against this contract; if the real system cannot express acknowledgement or status semantics, the contract — not the Domain — is versioned.

## 16. Failure semantics

Differentiate at minimum:

- no applicable knowledge;
- clarification required;
- generator failure (`KnowledgeFailure.MODEL_ERROR`);
- retrieval/storage failure;
- knowledge service unavailable / timeout / invalid response;
- handoff transport failure;
- stale/superseded turn.

Never turn infrastructure failure into «в базе нет информации».

If `knowledge` is unavailable and user requests human support, the handoff package must still be buildable from persisted/raw known fields.

## 17. Security boundaries

- raw organizer data not committed;
- model weights not committed;
- secrets only through environment/secret mounts;
- user/document text treated as data, never executable prompt policy;
- sanitized Markdown rendering;
- authorization/ownership checked server-side in `api`: every case, notification and archive row carries `owner_id`; the browser presents an opaque anonymous owner session issued by `api` in an `HttpOnly` cookie (`contracts/web-api-v0.md §13`) — no accounts, no passwords in P0;
- inbound support webhook, when enabled, verifies an HMAC signature over the raw body with a secret from environment; unsigned or replayed (`external_revision` already seen) requests are rejected before any state change;
- `knowledge` is not exposed outside the Compose network;
- `knowledge` DB role: privileges only on its own tables; no access to `api` tables in any form;
- logs contain IDs/reason codes, not chain-of-thought/private full prompts by default;
- demo mode controlled server-side in `api`.

## 18. Observability

Each turn should eventually expose a trace with:

```text
trace_id
case_id / turn_id / revision
stage/event timestamps
snapshot/retrieval config
retrieved fragment IDs
reason codes
model runtime/version
latency buckets (per stage, including knowledge round-trip)
final decision
error category
```

`trace_id` is generated in `api`, propagated to `knowledge` via `X-Trace-Id`, and logged by both. Do not log hidden reasoning.

## 19. Architecture changes

Any change that adds a service, queue, database, product runtime agent, model provider, new state dimension or moves ownership of a module across the `api`/`knowledge` boundary requires:

1. measured/explicit problem;
2. short ADR in `docs/adr/`;
3. failure/operational cost analysis;
4. update to `docs/stack.md` and this file;
5. skeptic review before merge/commit completion.
