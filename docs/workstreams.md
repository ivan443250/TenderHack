# Workstreams and ownership

Этот документ нужен, чтобы несколько coding agents/разработчиков могли работать параллельно без размывания границ. Он не заменяет `architecture.md` и contracts; он маршрутизирует работу и не является progress tracker.

## 1. Правило разделения

Каждый workstream должен иметь:

- одного runtime-owner;
- явные входы/выходы;
- frozen/shared contracts до параллельной реализации;
- собственные тесты;
- запрет на прямое чтение/изменение чужого state.

Если для задачи приходится менять shared contract — root/integrator сначала синхронизирует контракт, затем независимые ветки продолжают работу.

## 2. Карта блоков

| Блок | Runtime owner | Основной output | Shared boundary |
|---|---|---|---|
| A. Support Core | `.NET api` | state, Decision, public API, SSE | web-api-v0, knowledge-v0 |
| B. Knowledge online | Python `knowledge` | facts/evidence/draft/verify | knowledge-v0 |
| C. Ingestion / KB | Python `knowledge-worker` | snapshots/fragments/cards/index | knowledge-owned schema |
| D. Web / chat UX | React `web` | chat timeline/source/handoff/feedback/product UX | web-api-v0 |
| E. Handoff integration | `.NET api-worker` | durable external handoff/status sync | support-adapter-v0 |
| F. Quality analytics | Python `knowledge-worker` | quality evaluations/issue groups/product analytics | knowledge-v0 quality endpoints |
| G. Evals / Ops | cross-cutting | regression/compose/observability/model certification | all frozen contracts |

## 3. A — Support Core (`src/support-core`)

**Owns:** `Case`, `Turn`, revisions, all state enums, `TurnOrchestrator`, moderation policy (warning-first threshold), routing policy, idempotency, handoff aggregate/outbox, `IngestHandoffStatus` use-case, completion/archive, notifications table + owner SSE stream, owner session cookie, feedback (four signals), HTTP/SSE/authz, support webhook endpoint.

**Reads first:** `product-spec.md`, `architecture.md §4–8`, `contracts/web-api-v0.md`, `contracts/knowledge-v0.md`, `contracts/support-adapter-v0.md`; для product recovery/context также `product-experience.md`.

**May depend on:** generated knowledge client in Infrastructure, support adapter port, API-owned PostgreSQL tables.

**Must not:** read `kb_*`/`quality_*`; embed Python decision logic; expose generated transport DTO into Domain/Application; let browser state become authoritative.

**Ready when:** Domain state tests pass, deterministic Knowledge fixtures can drive answer/clarify/handoff/technical-error branches, public API snapshot can restore UI after reload. Real Knowledge flow is tested separately; fixtures are not a maturity statement.

## 4. B — Knowledge online (`src/knowledge`)

**Owns:** understand, exact extraction, retrieval, RRF/dedupe/rerank, answerability evidence assessment, draft, verify, source resolution, model adapters.

**Reads first:** `product-spec.md §6–13`, `architecture.md §9–11`, `contracts/knowledge-v0.*`, `stack.md`, `quality.md §3–5`.

**May depend on:** knowledge-owned PostgreSQL schema and local inference runtime.

**Must not:** produce `Decision`, `HandoffStatus`, routing outcome, support status; call `api`; read API-owned tables; retrieve historical resolution as normative evidence.

**Current state / readiness:** deterministic fixture mode remains for contract/decision tests, while real ingestion/retrieval/answerability/draft/verify implementations already exist. The active runtime milestone is Model Stack v2 certification: Giga backfill and B1/B2 retrieval measurements, Querit B3 only after artifact/provider verification, Qwen3.8 structured-generation smoke, latency/memory/offline checks. Do not describe Knowledge as “stub-only”.

## 5. C — Ingestion / Knowledge Base (`knowledge-worker`)

**Owns:** document inventory/version/hash, parsing, structure recovery, fragments, source anchors, condition cards, embeddings/index build, immutable snapshot publication.

**Reads first:** `product-spec.md §6–9`, `architecture.md §8–9`, `quality.md §3, §12`, `stack.md`.

**Must preserve:** filename/document version, PDF page and document page when known, section/anchor, table-row context, text hash, review status.

**Must not:** silently drop critical tables/conditions; mix historical support corpus into normative snapshot; introduce OCR everywhere before measuring extraction failures; auto-promote historical human resolution into normative knowledge.

**Ready when:** every demo source button can resolve `fragment_id → exact source metadata/text`, and critical cards have condition-change regressions.

## 6. D — Web / Chat (`src/web`)

**Owns:** presentation only — chat timeline, source drawer/button, progress stages, moderation warning rendering, handoff CTA/status widget (status + stage/specialist rows from server facts), «Завершить обращение» control, completion/feedback widget, case list with «Архив», notification toast/badge/inbox + Web Notifications API integration, analytics screens and selected enhancements from `product-experience.md`.

**Reads first:** `contracts/web-api-v0.md`, `product-spec.md §3, §14, §16–18`, `product-experience.md`, `architecture.md §5.8–5.9, §13–14`.

**Must not:** call `knowledge`; infer `RESOLVED` from `ANSWER`; invent specialist/stage/SLA/Portal context or placeholder rows; decide the moderation threshold locally; persist authoritative lifecycle only in local state; use Web Push/service-worker push; render a product enhancement as functional before required server facts/contracts exist.

**Current state / readiness:** current Web is still a minimal shell. Target UX is accepted documentation, not proof of implementation. A completed surface must restore from server state/SSE and every visible authoritative source/status/context fact must come from a permitted source.

## 7. E — Handoff / Support integration (`api-worker`)

**Owns:** outbox delivery, retry/backoff, `SubmitAsync` calls, `handoff-status-sync` polling job (`GetStatusAsync`), demo adapter (submit modes + `staged` status script), quality pushes (`turns`, `feedback`, `completions`).

**Reads first:** `contracts/support-adapter-v0.md`, `product-spec.md §17`, `architecture.md §7, §12, §15`, ADR-0002, OD-003.

**Must not:** mark accepted before adapter acknowledgement; fabricate assignee/stage/terminal; treat a poll failure as a status change; require knowledge availability to submit a prepared handoff; call demo behavior a real Portal integration.

**Ready when:** success/timeout/failure/simulated submit paths and the `staged` status script are deterministic, idempotent by `(handoff_id, external_revision)` and reflected to browser through API-owned state/events; terminal status completes the case once.

## 8. F — Quality analytics (`knowledge-worker`)

**Owns:** persisted pushed quality cases, response-quality rubric, feedback analytics, conservative issue groups, limitations/hypotheses; when implemented, the analytic inputs for Knowledge Gap Radar / Emerging Issue Detector.

**Reads first:** `quality.md §9–10`, `product-spec.md §18–19`, `product-experience.md §7–8`, `contracts/knowledge-v0.*`.

**Must keep separate:** response quality, resolution outcome, specialist feedback, information-quality feedback, infrastructure failure and knowledge insufficiency.

**Must not:** publish personal employee ranking in P0; treat negative feedback as proof of employee fault; mutate support state; call an issue group a confirmed incident; auto-publish a proposed knowledge card.

**Ready when:** every result has provenance/limitation and analytics can run with only pushed fixtures/data owned by Knowledge (without API DB access).

## 9. G — Evals / Ops

**Owns:** decision fixtures, retrieval gold, moderation regression, quality audit set, model/runtime certification evidence, E2E, Docker Compose, health checks, trace/log consistency.

**Reads first:** `quality.md`, `execution-plan.md` as reference, `stack.md`, active plans, all contracts touched by the test.

**Must distinguish:** historical benchmark vs current model stack; selected artifact vs certified runtime; deterministic fixture vs real-model run; container existence vs readiness.

**Ready when:** claimed demo/runtime capabilities have reproducible evidence and the critical flow does not depend on external AI/search APIs.

## 10. Parallelization rules

Good parallel split after contracts are frozen:

```text
A Support Core       ─────┐
B Knowledge real/fixture  ├─ contract tests ─→ E2E
D Web                     │
E Handoff                 │
C Ingestion ──────────────┘
F Quality can progress from pushed fixtures independently
G independently verifies claimed gates
```

Do not parallelize uncoordinated edits to:

- `Decision`/state enum semantics;
- `knowledge-v0.openapi.yaml`;
- public timeline event shapes and notification shapes;
- database ownership;
- moderation policy/threshold;
- handoff acknowledgement and status-ingestion semantics;
- completion paths (who may set `RESOLVED`);
- model/runtime policy in ADR-0003;
- product enhancement that requires new authoritative server facts/contracts.

Those are integration-owner changes and need one coordinated diff + skeptic review.
