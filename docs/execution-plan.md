# Execution plan — hackathon critical path

## 1. Planning assumptions

- Working window: about 40 useful hours.
- Default team shape in the reviewed spec: 4 people.
- Priority order: working vertical slice → correctness of branching/states → quality analytics → retrieval improvements → visual polish.
- Do not spend the first half of the hackathon perfecting models in isolation.
- Every phase ends with an observable gate. If a gate fails, stop adding scope and repair it.

## 2. Roles

| Role | Primary ownership | Must not decide alone |
|---|---|---|
| Backend / integration (.NET `api`) | states, orchestrator, `Decision`, persistence of case tables, jobs, handoff, contract `v0` consumer | content of domain instructions / truth policy; changing `v0` without ML/data |
| ML / data (Python `knowledge`) | ingestion, retrieval, model serving, answerability/verify, quality analytics, evals, contract `v0` provider | whether its own model is “good enough” without independent evaluation; any decision-like output |
| Frontend / design | chat, source view, states, read-only analytics | server lifecycle semantics / fake local status |
| Product / QA / domain | knowledge cards, rubric, gold cases, BPMN, demo | implementation architecture without engineering review |

If team size differs, preserve ownership separation conceptually. Cut P1 before combining correctness and review responsibilities into one unchallenged decision maker.

## 3. 0–2 h — Reality and hardware gate

Parallel work:

### Backend (.NET `api`)

- lock initial API/state vocabulary;
- **agree and freeze contract `v0` with ML/data within the first hour** (`architecture.md §10`): endpoint list, field names, `X-Trace-Id` headers, failure categories;
- generate the C# client from the stub OpenAPI; wire `IKnowledgeService` against it;
- create minimal EF Core skeleton for `api`-owned tables only after contracts are understood;
- define case/turn/handoff IDs and idempotency behavior;
- first `Domain` invariant tests (state transitions) before any HTTP endpoint.

### ML/data (Python `knowledge`)

- stand up `knowledge` FastAPI with **stub responses** for every `v0` endpoint (plausible JSON, fixed fragment IDs) so `api` and `web` can integrate before models exist;
- inventory all source files/data;
- run hardware smoke test;
- load candidate local generator/embedding/reranker;
- record GPU/CPU/RAM/runtime/model revisions;
- inspect critical PDF/table extraction;
- PostgreSQL admin bootstrap for `pgvector`/`pg_trgm`, then Alembic baseline for knowledge-owned tables.

### Frontend

- create minimal shell and case/source layout;
- agree server-state contract; no mock lifecycle logic that diverges from backend.

### Product/QA

- create first gold/regression cases from critical domain branches;
- verify requirements and line/channel semantics with experts where possible.

### Gate G0

Must be true:

- local inference boots; no external AI API required;
- exact hardware limits known;
- team agrees on API/state terms;
- contract `v0` is frozen and served by `knowledge` stubs; `api` calls it end-to-end with trace headers;
- table ownership per `architecture.md §8` is reflected in both migration baselines; `knowledge` test suite runs against a database without `api` tables;
- data/knowledge inventory exists;
- no stale architecture assumption blocks implementation.

If model/runtime fails here, switch once to the documented fallback. Do not carry two serving stacks forward.

## 4. 2–8 h — First real vertical slice

Goal: **real question → real source → answer/abstain → browser**.

Build:

- document registry + extraction baseline;
- initial semantic fragments with source/page anchors;
- PostgreSQL schema/migrations (EF Core for `api`, Alembic for `knowledge`);
- lexical + exact + dense baseline behind real `/v0/retrieve`, replacing the stub;
- basic case/message persistence;
- simple `TurnOrchestrator` path in .NET calling `understand → retrieve → answerability → draft → verify`;
- source drawer/open source action;
- first condition cards for the highest-risk/high-frequency cases;
- first regression/gold suite.

### Gate G1

From a fresh browser/session:

1. user asks a real question from corpus;
2. system retrieves an actual fragment;
3. a supported answer is displayed with resolvable source;
4. one known-unanswerable case does **not** hallucinate;
5. state survives page reload.

No G1 → no analytics dashboard, no elaborate agents, no styling sprint.

## 5. 8–16 h — Decision correctness, moderation, routing, handoff

Build in parallel:

### Knowledge/ML

- hybrid retrieval + RRF;
- reranker;
- exact codes/status handling;
- answerability gate;
- post-generation support validation;
- typo normalization regression cases.

### Backend

- full decision enum incl. `MODERATION_WARNING`;
- conversation (incl. `CLOSED_SUPPORT`) / resolution / turn / handoff states, `moderation_warning_count`;
- owner session cookie and owner scoping on every case endpoint;
- superseded turn logic;
- handoff outbox + controlled demo adapter (`api-worker`), `SubmitAsync` modes;
- `IHandoffAdapter.GetStatusAsync` + `handoff-status-sync` job + `IngestHandoffStatus` (ADR-0002); demo `staged` script;
- idempotency semantics, incl. `(handoff_id, external_revision)`;
- failure categories, including per-stage `KnowledgeFailure` mapping;
- outbox push of quality-turn / feedback payloads to `knowledge` (`/v0/quality/turns`, `/v0/quality/feedback`) — needed by the 16–24 h phase;
- boundary check at gate: no `Decision`/handoff logic in `knowledge`, no cross-runtime table access (ADR-0001 §6 trigger).

### Moderation/routing

- profanity normalizer + rules/version;
- warning-first threshold policy (`Moderation:CloseAfterWarnings`), warning text as server event;
- contextual ambiguity path only where justified;
- service need + line/channel policy + reason codes.

### Frontend

- semantic stages;
- clarification;
- «подтверждённого ответа нет» + «Обратиться к оператору поддержки»;
- handoff review/confirm;
- status widget: pending/accepted/simulated/failed + stage/specialist rows from server facts only;
- moderation warning and close;
- technical error.

### Gate G2

Critical branches work without manual DB editing:

- `ANSWER` with source button that opens the fragment;
- `CLARIFY` when one condition changes the branch;
- `HANDOFF_OFFER` on insufficient/factual-state cases, with the operator button;
- `ANSWER_AND_HANDOFF` when instruction itself requires support;
- moderation warning, then close on repeat;
- handoff success and failure; `staged` demo shows stage/specialist changing live via `HANDOFF_STATUS`;
- `ANSWER` does not resolve the case.

## 6. 16–24 h — Quality contour and read-only analytics

Build:

- completion: `POST .../complete`, adapter terminal → `CLOSED_SUPPORT`, `CASE_COMPLETED` / `FEEDBACK_REQUESTED` events;
- archive: `GET /api/v0/cases?status=`, read-only case view;
- notifications: `notifications` table, `GET /api/v0/notifications`, owner SSE stream, ack; web toast/badge + Web Notifications API when hidden;
- feedback widget with four signals (`specialist_rating`, `information_quality_rating`, `solved`, `comment_text`), once per case;
- outbox push `/v0/quality/feedback` (extended fields) and `/v0/quality/completions`;
- source-type/data-sufficiency model;
- quality rubric (`0/1/2/UNKNOWN/NA`);
- quality evaluator with evidence/limitations;
- historical topic/group baseline;
- repeated-problem cards with representative examples and explicit hypotheses;
- specialist-feedback slices by issue group / line / `specialist_ref` with `n` and limitations — no ranking;
- protected read-only analytics UI;
- simulated human reply only if needed to demonstrate the methodology, clearly labelled.

### Gate G3

Demo can show:

1. one answer/record with a useful quality audit;
2. one case where insufficient data yields `UNKNOWN`, not fake zero;
3. one repeated-problem group with real `n` and examples;
4. negative feedback is not automatically converted into employee blame;
5. a case completed by the `staged` demo support while the user is elsewhere → notification arrives, case lands in «Архив», feedback widget opens with the specialist row labelled demo;
6. a case completed by the user («Завершить обращение») → same completion/feedback path, `CLOSED_USER`.

## 7. 24–32 h — Evaluation, regression and hardening

Priority is proving behavior, not adding features.

Work:

- freeze a dev/blind split;
- run retrieval baselines;
- run decision/moderation/routing evals where labels permit;
- create confusion/error tables;
- repair critical false answers;
- test stale turn, retries, idempotency and handoff timeouts;
- offline/no-external-internet run;
- performance/concurrency smoke test;
- clean logging and trace IDs;
- fix critical source-opening/parsing failures.

### Gate G4

- actual benchmark results exist with `n` and config;
- no unresolved critical regression in domain scenarios;
- no false success on handoff/state paths;
- demo does not depend on external AI/internet;
- skeptic review has no unresolved critical/high issue on core flow.

## 8. 32–36 h — Freeze, deployment, BPMN, defense package

**Feature freeze.**

Do:

- verify clean `docker compose`/documented start on fresh environment;
- migrations/seed/import runbook;
- health checks;
- final architecture diagram;
- BPMN for request handling and quality analytics;
- license/model/source manifest;
- README synchronization;
- presentation built from actual metrics/limitations;
- code cleanup only where it reduces review risk.

### Gate G5

A teammate who did not implement the relevant module can start and demo the project from documentation.

## 9. 36–40 h — Release mode

Only:

- critical bug fixes;
- verification after fixes;
- repeat demo;
- pitch rehearsal;
- code review rehearsal;
- prepare likely judge questions with exact evidence.

Do not introduce a new library/model/service after G5 unless the project otherwise cannot demo.

### Release gate

Several consecutive demo runs without manual hidden corrections.

## 10. What to cut first

If behind schedule:

1. OCR/vision for user attachments.
2. Semantic clustering beyond stable frequency/rule grouping.
3. Trainable topic classifier.
4. Extra visualization and optional sources.
5. Extra model/runtime adapters.
6. Any P1 feature.

## 11. What must not be cut

- correct orthogonal state model;
- all provided core knowledge indexed at baseline;
- source provenance;
- answer/clarify/handoff distinction;
- honest technical error vs no-answer distinction;
- explicit handoff status and real/demo separation, incl. status ingestion from the adapter (no fabricated stage/specialist);
- explicit completion + in-app notification + feedback widget (the 7-point chat UX is the scored functionality);
- minimum quality methodology;
- critical domain regressions;
- reproducible local run;
- tests/evals for high-risk paths.

## 12. Agent execution inside each phase

Coding agents use `docs/agent-workflow.md`.

Recommended parallel split during each phase:

- root agent owns current gate and shared contracts;
- Scout agents inspect data/docs/contracts read-only;
- Specialist implementers work only on non-overlapping bounded modules;
- Verifier independently checks the gate;
- Skeptic reviews the integrated diff before a gate is declared complete.

Do not let an agent optimize its own subsystem and self-certify the phase. Gate ownership remains with root/TL.

## 13. Change policy during hackathon

A new idea can enter P0 only if it:

1. satisfies a formal requirement not currently covered; or
2. fixes a measured critical/high failure; or
3. materially improves the 5-minute demo without destabilizing a gate.

Everything else goes to P1/post-hackathon notes.

## 14. Judge/code-review readiness

At any time after G2 the repository should be moving toward answering:

- Why this state exists?
- Why this source is authoritative?
- Why did the system answer rather than abstain?
- What happens on model/storage/handoff failure?
- Where are the tests/evals?
- What is measured vs planned?
- Why is this dependency/service necessary?
- Can it run without external AI/internet?

If the team cannot answer one of these from code/docs/traces, that is engineering debt more important than a new feature.
