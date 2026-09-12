# Support adapter contract v0

Статус: **frozen v0 semantics**. Контракт определяет boundary между `.NET Application` и внешней/демонстрационной системой поддержки. Историческая привязка к Gate G2 объясняет порядок первоначальной реализации, но не делает контракт временным: demo adapter сегодня и реальный Portal adapter позже должны соблюдать эту семантику либо явно версионировать boundary.

## 1. Ownership

`.NET api` владеет prepared package, handoff state, outbox, idempotency и отображаемым UI state.

Adapter **не** принимает продуктовых решений. Он только принимает подготовленное обращение и возвращает acknowledgement/status facts.

## 2. Request

Концептуально:

```text
HandoffRequest
- handoff_id
- case_id
- channel
- dispatch_queue
- summary
- user_reported_context
- verified_portal_context?
- already_tried[]
- unknown_fields[]
- sources_checked[]
- handoff_reason
- relevant_message_ids[]
- engineering_review_suggested
- integration_mode
```

Rules:

- `summary` editable before confirm;
- user-reported and verified context are distinct;
- unknown stays unknown;
- no invented SLA/contact/specialist;
- package can be built while knowledge is unavailable.

## 3. Port

Application port concept — one outbound and one inbound-pull method:

```csharp
public interface IHandoffAdapter
{
    Task<HandoffAck> SubmitAsync(
        HandoffRequest request,
        string idempotencyKey,
        CancellationToken ct);

    // Returns null when the adapter has no status API (then only the webhook, §6.2, can deliver updates).
    Task<HandoffStatusSnapshot?> GetStatusAsync(
        HandoffStatusQuery query,
        CancellationToken ct);
}
```

```text
HandoffStatusQuery
- handoff_id
- external_case_id?
- last_known_external_revision?
```

The same logical handoff retry reuses the same stable idempotency key.

## 4. Acknowledgement

Conceptual result:

```text
HandoffAck
- outcome: ACCEPTED | REJECTED
- integration_mode: REAL | SIMULATED
- external_case_id?
- assigned_specialist?
- stage?
- received_at
- provider_code?
- safe_message?
```

`assigned_specialist`:

```text
- ref?            opaque external identifier
- display_name?   only when returned by adapter
```

`stage`:

```text
- code
- display_name
```

Stage values are adapter facts, not a universal Domain enum. Preserve unknown external codes as observable metadata when safe; UI displays only server-approved mapping/display text.

## 5. Mapping to Domain handoff state

- prepare only → `NOT_REQUESTED` (package exists, not submitted);
- confirmed + outbox committed → `PENDING`;
- `outcome=ACCEPTED`, `integration_mode=REAL` → `ACCEPTED`;
- `outcome=ACCEPTED`, `integration_mode=SIMULATED` → `SIMULATED_ACCEPTED`;
- terminal submit failure after policy/retries → `FAILED`;
- timeout / lost acknowledgement does **not** become accepted.

`REJECTED` is mapped by `.NET` policy to an honest failed/rejected user state; do not fabricate acceptance.

`HandoffStatus` does not gain new values for stage/terminal: those are separate nullable facts on the handoff (`architecture.md §6`), and completion is expressed through `ConversationStatus`/`ResolutionStatus` (§6.3).

## 6. Status updates (inbound)

After acceptance, `api` needs status facts from the support side. Both channels below produce the same record and enter the same Application use-case `IngestHandoffStatus`; the Domain never knows which channel delivered a fact.

```text
HandoffStatusSnapshot
- handoff_id                 our id, echoed back
- external_case_id
- integration_mode: REAL | SIMULATED
- external_revision          monotonic per external case (int or opaque string, adapter-defined)
- occurred_at
- stage?                     { code, display_name }
- assigned_specialist?       { ref, display_name? }
- terminal?                  RESOLVED | CLOSED_UNRESOLVED | CANCELLED
- provider_code?
- safe_message?
```

Idempotency key: `(handoff_id, external_revision)`. A revision already applied is a no-op; a revision lower than the last applied is ignored and logged. A snapshot whose `handoff_id`/`external_case_id` do not match the persisted handoff is rejected (test 7).

### 6.1. Channel A — polling (P0, mandatory)

`api-worker` job `handoff-status-sync`:

- selects handoffs with `HandoffStatus ∈ {ACCEPTED, SIMULATED_ACCEPTED}` and `terminal = null`;
- calls `GetStatusAsync` with the last known revision;
- backoff per handoff: `Support:StatusPoll:Initial` (default 15 s) → doubling → `Support:StatusPoll:Max` (default 5 min); stops at terminal status or after `Support:StatusPoll:Ttl` (default 7 days, after which the handoff is marked `stale=true` and the UI shows «статус не обновляется»);
- adapter timeout/5xx counts as a failed attempt, never as a status change.

### 6.2. Channel B — webhook (optional, real adapters only)

`POST /api/v0/integrations/support/status` on `api`:

- body = `HandoffStatusSnapshot` JSON;
- `X-Support-Signature: sha256=<hex HMAC over raw body>` with `Support:Webhook:Secret` from environment; missing/invalid signature → `401`, no state change;
- `X-Support-Timestamp` within ±5 min, otherwise `401`;
- `202` after the fact is persisted (or found duplicate); `404` for unknown `handoff_id`; `409` for `handoff_id`/`external_case_id` mismatch;
- endpoint is not registered at all unless `Support:Webhook:Enabled=true`.

The demo adapter never uses the webhook.

Allowed observable fields are exactly those of `HandoffStatusSnapshot`. Do not infer a specialist or support stage from queue/routing alone; do not fabricate a terminal status because polling has stopped.

### 6.3. Mapping to case state

| Snapshot | Effect (`architecture.md §5.8`) |
|---|---|
| new `stage` / `assigned_specialist` | `HANDOFF_STATUS` case event, `HANDOFF_UPDATED` notification |
| `terminal = RESOLVED` | `ResolutionStatus = RESOLVED`, `ConversationStatus = CLOSED_SUPPORT` (if still `ACTIVE`), `CASE_COMPLETED`, `FEEDBACK_REQUESTED` |
| `terminal = CLOSED_UNRESOLVED` | `ResolutionStatus = UNRESOLVED`, same closure/events |
| `terminal = CANCELLED` | `ResolutionStatus` unchanged (`UNKNOWN` unless user set it), `CLOSED_SUPPORT`, `CASE_COMPLETED`, `FEEDBACK_REQUESTED` |

A terminal fact arriving on a case that is already `CLOSED_USER` updates handoff facts and `ResolutionStatus` only if it was `UNKNOWN`; it does not emit a second `CASE_COMPLETED`.

## 7. Delivery semantics

- outbox + `api-worker` delivery is at-least-once;
- adapter call must be idempotent by provided key or be protected by our adapter implementation;
- retries use bounded backoff/config;
- adapter timeout, 5xx/transport failure and explicit rejection are distinguishable in logs/events;
- no exactly-once claim.

## 8. Demo adapter

Demo mode is selected server-side by configuration (`Support:Adapter=demo`).

Submit scenarios (`Support:Demo:Submit`):

- `success` → simulated accepted with deterministic `external_case_id` derived from `handoff_id`;
- `timeout`;
- `failure` / `rejection`.

Status scenarios (`Support:Demo:Status`):

- `none` → `GetStatusAsync` returns the acceptance snapshot forever (no stage, no specialist);
- `staged` → deterministic script keyed by `handoff_id`, revisions `1..4`, each becoming visible after `Support:Demo:StageDelaySeconds × revision` (default 10 s):

```text
rev 1  stage QUEUED        «В очереди»
rev 2  stage ASSIGNED      «Назначен специалист»   assigned_specialist { ref: "demo-1", display_name: "Демо-специалист" }
rev 3  stage IN_PROGRESS   «В работе»
rev 4  terminal RESOLVED   stage RESOLVED «Решено»
```

Every simulated snapshot carries `integration_mode=SIMULATED`; browser must visibly distinguish it from a real submission and label the specialist row as demo. The script is the same on every run so E2E and demo rehearsals are reproducible.

## 9. Security / privacy

- send only fields needed for support;
- do not send hidden prompts/CoT/model internals;
- secrets/credentials live outside git;
- adapter payload/logging must avoid unnecessary raw personal/private data;
- external display text is treated as untrusted data and sanitized before browser rendering.

## 10. Contract tests

Minimum:

1. repeated delivery with same idempotency key does not create a second logical handoff;
2. timeout leaves state non-accepted and retryable;
3. accepted ack is the only path to accepted state;
4. simulated ack maps to `SIMULATED_ACCEPTED`;
5. missing specialist/stage stays null;
6. knowledge unavailable does not prevent package submission from persisted data;
7. status update cannot mutate a different handoff/case (`handoff_id`/`external_case_id` mismatch → rejected);
8. same `(handoff_id, external_revision)` delivered twice (poll + webhook, or poll twice) is applied once;
9. lower `external_revision` than last applied is ignored;
10. `GetStatusAsync` timeout/5xx does not change stage/terminal and does not stop the poll schedule before TTL;
11. terminal `RESOLVED` on an `ACTIVE` case → `CLOSED_SUPPORT` + `RESOLVED` + exactly one `CASE_COMPLETED`; on a `CLOSED_USER` case → no second `CASE_COMPLETED`;
12. `staged` demo script produces revisions 1..4 in order and is identical across two runs for the same `handoff_id`;
13. webhook: unsigned / bad signature / stale timestamp → `401` and no persisted row; endpoint absent when disabled.
