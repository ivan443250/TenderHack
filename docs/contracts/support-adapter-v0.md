# Support adapter contract v0

Статус: **semantic freeze for G2**. Контракт определяет boundary между `.NET Application` и внешней/демонстрационной системой поддержки. Реализация может быть demo adapter сегодня и реальный Portal adapter позже, но Domain/Application semantics не меняются.

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

## 3. Submit API / port

Application port concept:

```csharp
Task<HandoffAck> SubmitAsync(
    HandoffRequest request,
    string idempotencyKey,
    CancellationToken ct);
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

## 6. Status updates

If the real/demo adapter supports subsequent status updates, Infrastructure maps them into API-owned handoff/case events.

Allowed observable fields:

- external case id;
- assigned specialist, if supplied;
- provider stage/status, if supplied;
- occurred/received timestamp;
- terminal resolved/closed fact only when the adapter contract actually provides it.

Do not infer a specialist or support stage from queue/routing alone.

## 7. Delivery semantics

- outbox + `api-worker` delivery is at-least-once;
- adapter call must be idempotent by provided key or be protected by our adapter implementation;
- retries use bounded backoff/config;
- adapter timeout, 5xx/transport failure and explicit rejection are distinguishable in logs/events;
- no exactly-once claim.

## 8. Demo adapter

Demo mode is selected server-side by configuration.

Required deterministic scenarios:

- success → simulated accepted with deterministic external id;
- timeout;
- failure/rejection;
- optional staged update scenario for UI demo.

Every simulated acknowledgement persists `integration_mode=SIMULATED`; browser must visibly distinguish it from real submission.

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
7. status update cannot mutate a different handoff/case.
