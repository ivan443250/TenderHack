# Architecture

## 1. Architectural goal

MVP must be easy to reason about, easy to run on one Linux host, locally inferenced, and explicit about failure states. The architecture optimizes for **correctness, observability and agent legibility**, not service count.

Chosen shape: **modular monolith** for API/application/domain logic + a separate worker process built from the same Python codebase + a separate React web app + PostgreSQL.

Do not reintroduce a .NET API layer or an extra ML microservice unless new hard requirements justify the split.

## 2. Target repository layout

After implementation begins:

```text
apps/
  api/
    pyproject.toml
    app/
      api/             HTTP/SSE boundaries
      domain/          pure state/invariants
      application/     use-cases/orchestration
      knowledge/       ingestion/retrieval/verification
      moderation/
      routing/
      quality/
      analytics/
      persistence/
      inference/
      settings/
  web/
    package.json
    src/
worker/
  # thin entrypoint using apps/api application modules
evals/
  retrieval/
  decisions/
  moderation/
  quality/
  e2e/
scripts/
docs/
```

If a simpler physical layout improves speed, keep these **logical boundaries** even if directories differ. Update this document if that happens.

## 3. System context

```text
Browser
   │ HTTP/SSE
   ▼
FastAPI
   ├── Case orchestration
   ├── Moderation
   ├── Query understanding
   ├── Knowledge retrieval / answerability
   ├── Routing / handoff
   ├── Feedback
   └── Read-only analytics
   │
   ├─────────────► PostgreSQL 16
   │                cases/messages/events
   │                knowledge metadata/fragments
   │                FTS/pg_trgm/pgvector
   │                feedback/quality/jobs/outbox
   │
   ├─────────────► local embedding/reranker runtime
   └─────────────► local generation runtime (vLLM)

Worker (same application code)
   ├── ingestion
   ├── embeddings
   ├── outbox/handoff adapter
   └── post-case quality/analytics
```

No business decision may exist only inside frontend state or prompt prose.

## 4. Dependency direction

Recommended logical flow:

```text
API boundary
   ↓
Application use-case / orchestrator
   ↓
Domain state + policies
   ↓
Ports
   ↓
Persistence / retrieval / inference / adapters
```

The domain must not import FastAPI, SQLAlchemy, vLLM client code or React concepts.

Application code may depend on typed domain contracts, not concrete database sessions scattered through handlers.

Infrastructure adapters parse external/raw shapes at their boundary and return typed internal models.

Avoid ceremonial Clean Architecture layers that only proxy calls. A boundary exists only where it protects a real invariant or replacement point.

## 5. Core modules

### 5.1. Cases

Owns:

- case identity;
- conversation state;
- turn revisions;
- decision history;
- resolution status;
- handoff state;
- feedback references.

Critical rule: state transitions are explicit methods/functions with preconditions, not arbitrary field mutation.

### 5.2. Orchestration

Owns per-turn state machine:

```text
persist input
→ moderate
→ direct human-request check
→ understand context
→ exact rules/entities
→ retrieve knowledge
→ answerability
→ generate draft if allowed
→ verify draft
→ persist final decision atomically
```

Post-case quality runs asynchronously and must not block the user response.

### 5.3. Knowledge

Owns two physically/logically separated corpora:

- normative answer corpus;
- historical analytical corpus.

No shared convenience query that lets generation accidentally retrieve historical resolutions as normative evidence.

### 5.4. Moderation

Deterministic-first. LLM/context check only for ambiguity. Produces a policy outcome, rule/version and traceable match metadata.

### 5.5. Routing

Owns `service_need`, `recommended_line`, `dispatch_queue`, `engineering_review_suggested`, `reason_codes` and channel policy.

It must not pretend that inferred support-line labels from history are official ground truth.

### 5.6. Handoff

Owns:

- editable prepared package;
- `PENDING / ACCEPTED / SIMULATED_ACCEPTED / FAILED`;
- adapter idempotency key;
- outbox delivery;
- external ticket ID when real/simulated adapter returns one.

### 5.7. Quality / Analytics

Read-only with respect to support work. Produces evaluation/profile/hypothesis artifacts with provenance and limitations. Does not update employee ratings or publish knowledge automatically.

## 6. State model

Keep orthogonal dimensions:

```python
ConversationStatus = ACTIVE | CLOSED_USER | CLOSED_MODERATION
ResolutionStatus   = UNKNOWN | RESOLVED | UNRESOLVED
TurnStatus         = QUEUED | RUNNING | COMPLETED | FAILED | SUPERSEDED
HandoffStatus      = NOT_REQUESTED | PENDING | ACCEPTED | SIMULATED_ACCEPTED | FAILED
Decision           = ANSWER | CLARIFY | HANDOFF_OFFER | ANSWER_AND_HANDOFF | MODERATION_CLOSE | TECHNICAL_ERROR
```

Do not collapse these into one enum.

### Invariants

- One active revision is authoritative.
- Running old turn cannot publish after a newer user revision supersedes it.
- `ANSWER` cannot set `RESOLVED`.
- Handoff status changes to accepted only on adapter acknowledgement.
- Moderation close does not delete or revoke already accepted handoff.
- Failed AI turn does not silently mutate previous accepted handoff.

## 7. Idempotency and concurrency

All state-changing public commands accept an idempotency key where duplicate client retries are plausible.

Expected behavior:

- same key + same payload → same logical result;
- same key + different payload → conflict;
- only one active/accepted handoff per case/problem;
- worker is at-least-once; effects are idempotent by business key;
- turn publication unique by `turn_id` + revision;
- jobs use lease/heartbeat or equivalent recoverable claiming.

Do not claim exactly-once delivery.

## 8. Persistence

PostgreSQL is both system of record and initial retrieval engine.

Suggested schemas/tables, exact names can vary:

```text
cases
messages
turns
case_events
handoffs
outbox
feedback
quality_evaluations
issue_groups

kb_documents
kb_document_versions
kb_snapshots
kb_fragments
kb_condition_cards
kb_ingestion_runs

jobs
```

### Knowledge versioning

Every user-facing answer records at least:

- snapshot ID;
- fragment IDs;
- model/retrieval config version;
- final decision/reason codes.

This makes demo/debug reproduction possible.

## 9. Retrieval architecture

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
applicability / answerability
```

Do not call PostgreSQL FTS «BM25» unless BM25 is actually introduced.

No HNSW until exact retrieval latency is measured and shown to be a bottleneck on the real corpus.

## 10. Local inference boundary

The application sees narrow typed adapters:

```text
Embedder.embed(texts)
Reranker.score(query, passages)
Generator.generate_structured(request_schema)
Verifier.check_claim(claim, evidence)
```

No module gets a generic «LLM with arbitrary tools» interface.

The model cannot call SQL, shell or arbitrary HTTP.

### Generator budget

Default design target from reviewed spec:

- ordinary turn: ≤ 2 generative calls;
- complex verification path: ≤ 3;
- input context budget initially 8192 tokens;
- no silent truncation of conditions/exceptions.

These are config values and must be benchmarked on hardware.

## 11. Background worker

Separate process only because work duration/lifecycle differs, not because it is a separate business service.

Jobs:

- ingest/parse/index;
- embeddings batches;
- outbox delivery;
- quality audit;
- issue-group recomputation.

Start with PostgreSQL job table / `FOR UPDATE SKIP LOCKED` or equivalent. Do not add Redis/Kafka until measured need.

## 12. Frontend boundaries

Three primary surfaces:

1. **Case / chat** — question, progress, answer/clarification/handoff, feedback.
2. **Source view** — exact document/page/fragment.
3. **Read-only quality/issues** — quality evidence and repeated problem groups.

Technical trace for judges/team is protected detail, not end-user UI.

Frontend never invents lifecycle state. It renders server state and can reconnect using monotonic event IDs / polling fallback.

## 13. Progress transparency

Show semantic stages, not fake percentages:

```text
Проверяем запрос
Ищем применимые инструкции
Проверяем условия
Готовим ответ / передачу
```

Technical detail may expose event types and reason codes, but not chain-of-thought.

## 14. Handoff adapter

P0 adapter is controlled by the team and explicitly demo-labelled.

Interface concept:

```python
class HandoffAdapter(Protocol):
    async def submit(self, request: HandoffRequest, idempotency_key: str) -> HandoffAck: ...
```

Test modes: success, timeout, failure.

Real Portal integration remains a future adapter against an agreed contract.

## 15. Failure semantics

Differentiate at minimum:

- no applicable knowledge;
- clarification required;
- generator failure;
- retrieval/storage failure;
- handoff transport failure;
- stale/superseded turn.

Never turn infrastructure failure into «в базе нет информации».

If generator is unavailable and user requests human support, handoff package must still be buildable from persisted/raw known fields where possible.

## 16. Security boundaries

- raw organizer data not committed;
- model weights not committed;
- secrets only through environment/secret mounts;
- user/document text treated as data, never executable prompt policy;
- sanitized Markdown rendering;
- authorization/ownership checked server-side;
- logs contain IDs/reason codes, not chain-of-thought/private full prompts by default;
- demo mode controlled server-side.

## 17. Observability

Each turn should eventually expose a trace with:

```text
trace_id
case_id / turn_id / revision
stage/event timestamps
snapshot/retrieval config
retrieved fragment IDs
reason codes
model runtime/version
latency buckets
final decision
error category
```

Do not log hidden reasoning.

## 18. Architecture changes

Any change that adds a service, queue, database, product runtime agent, model provider or new state dimension requires:

1. measured/explicit problem;
2. short ADR/plan rationale;
3. failure/operational cost analysis;
4. update to `docs/stack.md` and this file;
5. skeptic review before merge/commit completion.
