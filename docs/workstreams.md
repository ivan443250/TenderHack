# Workstreams and ownership

Этот документ нужен, чтобы несколько coding agents/разработчиков могли работать параллельно без размывания границ. Он не заменяет `architecture.md` и contracts; он маршрутизирует работу.

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
| D. Web / chat UX | React `web` | chat timeline/source/handoff/feedback UI | web-api-v0 |
| E. Handoff integration | `.NET api-worker` | durable external handoff/status sync | support-adapter-v0 |
| F. Quality analytics | Python `knowledge-worker` | quality evaluations/issue groups | knowledge-v0 quality endpoints |
| G. Evals / Ops | cross-cutting | regression/compose/observability | all frozen contracts |

## 3. A — Support Core (`src/support-core`)

**Owns:** `Case`, `Turn`, revisions, all state enums, `TurnOrchestrator`, moderation policy (warning-first threshold), routing policy, idempotency, handoff aggregate/outbox, `IngestHandoffStatus` use-case, completion/archive, notifications table + owner SSE stream, owner session cookie, feedback (four signals), HTTP/SSE/authz, support webhook endpoint.

**Reads first:** `product-spec.md`, `architecture.md §4–8`, `contracts/web-api-v0.md`, `contracts/knowledge-v0.md`, `contracts/support-adapter-v0.md`.

**May depend on:** generated knowledge client in Infrastructure, support adapter port, API-owned PostgreSQL tables.

**Must not:** read `kb_*`/`quality_*`; embed Python decision logic; expose generated NSwag DTO into Domain/Application; let browser state become authoritative.

**Ready when:** Domain state tests pass, knowledge stub can drive answer/clarify/handoff/technical-error branches, public API snapshot can restore UI after reload.

## 4. B — Knowledge online (`src/knowledge`)

**Owns:** understand, exact extraction, retrieval, RRF/dedupe/rerank, answerability evidence assessment, draft, verify, source resolution, model adapters.

**Reads first:** `product-spec.md §6–13`, `architecture.md §9–11`, `contracts/knowledge-v0.*`, `stack.md`, `quality.md §3–5`.

**May depend on:** knowledge-owned PostgreSQL schema and local inference runtime.

**Must not:** produce `Decision`, `HandoffStatus`, routing outcome, support status; call `api`; read API-owned tables; retrieve historical resolution as normative evidence.

**Ready when:** all `/v0/...` contract endpoints work in deterministic stub mode, then real retrieval can replace stubs without changing the contract. The next runtime milestone is Model Stack v2 real certification: Giga backfill and B1/B2 retrieval measurements, Querit B3 only after artifact verification, Qwen3.8 structured-generation smoke, and latency/memory/offline checks.

## 5. C — Ingestion / Knowledge Base (`knowledge-worker`)

**Owns:** document inventory/version/hash, parsing, structure recovery, fragments, source anchors, condition cards, embeddings/index build, immutable snapshot publication.

**Reads first:** `product-spec.md §6–9`, `architecture.md §8–9`, `quality.md §3, §12`, `stack.md`.

**Must preserve:** filename/document version, PDF page and document page when known, section/anchor, table-row context, text hash, review status.

**Must not:** silently drop critical tables/conditions; mix historical support corpus into normative snapshot; introduce OCR everywhere before measuring extraction failures.

**Ready when:** every demo source button can resolve `fragment_id → exact source metadata/text`, and critical cards have condition-change regressions.

## 6. D — Web / Chat (`src/web`)

**Owns:** presentation only — chat timeline, source drawer/button, progress stages, moderation warning rendering, handoff CTA/status widget (status + stage/specialist rows from server facts), «Завершить обращение» control, completion/feedback widget, case list with «Архив», notification toast/badge/inbox + Web Notifications API integration, analytics screens.

**Reads first:** `contracts/web-api-v0.md`, `product-spec.md §3, §14, §16–18`, `architecture.md §5.8–5.9, §13–14`.

**Must not:** call `knowledge`; infer `RESOLVED` from `ANSWER`; invent specialist/stage/SLA or placeholder rows; decide the moderation threshold locally; persist authoritative lifecycle only in local state; use Web Push/service-worker push.

**Ready when:** reload reconstructs the same UI from `CaseSnapshot`; SSE reconnect can resume by event ID; every visible source/status came from server state.

## 7. E — Handoff / Support integration (`api-worker`)

**Owns:** outbox delivery, retry/backoff, `SubmitAsync` calls, `handoff-status-sync` polling job (`GetStatusAsync`), demo adapter (submit modes + `staged` status script), quality pushes (`turns`, `feedback`, `completions`).

**Reads first:** `contracts/support-adapter-v0.md`, `product-spec.md §17`, `architecture.md §7, §12, §15`, ADR-0002.

**Must not:** mark accepted before adapter acknowledgement; fabricate assignee/stage/terminal; treat a poll failure as a status change; require knowledge availability to submit a prepared handoff.

**Ready when:** success/timeout/failure/simulated submit paths and the `staged` status script are deterministic, idempotent by `(handoff_id, external_revision)` and reflected to browser through API-owned state/events; terminal status completes the case once.

## 8. F — Quality analytics (`knowledge-worker`)

**Owns:** persisted pushed quality cases, response-quality rubric, feedback analytics, conservative issue groups, limitations/hypotheses.

**Reads first:** `quality.md §9–10`, `product-spec.md §18–19`, `contracts/knowledge-v0.*`.

**Must keep separate:** response quality, resolution outcome, specialist feedback, information-quality feedback.

**Must not:** publish personal employee ranking in P0; treat negative feedback as proof of employee fault; mutate support state.

**Ready when:** every result has provenance/limitation and analytics can run with only pushed fixtures (without API DB access).

## 9. G — Evals / Ops

**Owns:** decision fixtures, retrieval gold, moderation regression, quality audit set, E2E, Docker Compose, health checks, trace/log consistency.

**Reads first:** `quality.md`, `execution-plan.md`, `stack.md`, all contracts touched by the test.

**Ready when:** fresh local Linux/Compose run executes the critical E2E set without external AI/search APIs.

## 10. Parallelization rules

Good parallel split after contracts are frozen:

```text
A Support Core  ─────┐
B Knowledge stub/real ├─ contract tests ─→ E2E
D Web                 │
E Handoff             │
C Ingestion ──────────┘
F Quality can progress from fixtures independently
```

Do not parallelize uncoordinated edits to:

- `Decision`/state enum semantics;
- `knowledge-v0.openapi.yaml`;
- public timeline event shapes and notification shapes;
- database ownership;
- moderation policy/threshold;
- handoff acknowledgement and status-ingestion semantics;
- completion paths (who may set `RESOLVED`).

Those are integration-owner changes and need one coordinated diff.
