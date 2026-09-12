# Browser ↔ API contract v0

Статус: **semantic freeze for G0/G1**. До появления `.NET` scaffold это нормативная семантика публичной browser boundary. После scaffold ASP.NET OpenAPI становится machine-readable artifact и должен оставаться совместимым с этим документом.

Browser ходит **только** в `api`. Он не знает адреса `knowledge`, PostgreSQL, inference runtime или support adapter.

## 1. Главная модель UI

99% пользовательского UX — один server-driven chat timeline:

```text
UserMessage
→ Progress / ModerationWarning / Clarification / AiAnswer / NoConfirmedAnswer
→ SourceLink(s)
→ HandoffOffer («Обратиться к оператору поддержки»)
→ HandoffStatus widget (status · stage · specialist, all server facts)
→ CaseCompleted (+ notification, §12)
→ FeedbackWidget
→ read-only/archive projection (case list, §3)
```

Frontend не выводит lifecycle state из анимации/текста ответа. Authoritative state приходит из `CaseSnapshot` + `case_events`/SSE. Уведомления — отдельный owner-level поток (§12), чтобы пользователь узнал о завершении и об обновлении статуса, даже если открыт другой чат или вкладка скрыта.

## 2. State vocabulary

Authority: `docs/architecture.md §6` / Domain enums.

```text
ConversationStatus = ACTIVE | CLOSED_USER | CLOSED_SUPPORT | CLOSED_MODERATION
ResolutionStatus   = UNKNOWN | RESOLVED | UNRESOLVED
TurnStatus         = QUEUED | RUNNING | COMPLETED | FAILED | SUPERSEDED
HandoffStatus      = NOT_REQUESTED | PENDING | ACCEPTED | SIMULATED_ACCEPTED | FAILED
Decision           = ANSWER | CLARIFY | HANDOFF_OFFER | ANSWER_AND_HANDOFF |
                     MODERATION_WARNING | MODERATION_CLOSE | TECHNICAL_ERROR
```

Moderation is warning-first (`product-spec.md §14`): the first confirmed violation in a case is `MODERATION_WARNING`, the next one is `MODERATION_CLOSE`. The threshold is server configuration; the browser only renders `moderation_warning_count` and the warning text.

`CLOSED_SUPPORT` is set when the support side (real or demo adapter) reports a terminal status. «Archived» in the UI is `conversation_status != ACTIVE`.

## 3. Public endpoint surface

Exact route names may be adjusted once Minimal API is scaffolded, but these capabilities and semantics are frozen.

| Capability | Suggested route | Semantics |
|---|---|---|
| Owner session | `POST /api/v0/session` | issues/refreshes the anonymous owner cookie (§13); idempotent |
| List cases | `GET /api/v0/cases?status=active\|archived` | owner-scoped list with `last_activity_at`, `unread_notifications` |
| Create case | `POST /api/v0/cases` | creates/restores one chat case; idempotent |
| Read case snapshot | `GET /api/v0/cases/{case_id}` | full server state for reload/recovery |
| Send user message | `POST /api/v0/cases/{case_id}/messages` | persists user input and starts new turn/revision; `409 CASE_CLOSED` on a completed case |
| Event catch-up | `GET /api/v0/cases/{case_id}/events?after=` | monotonic event replay/poll fallback |
| Event stream | `GET /api/v0/cases/{case_id}/events/stream` | SSE, supports reconnect via event id |
| Resolve source | `GET /api/v0/sources/{fragment_id}` | authz proxy to knowledge source endpoint |
| Prepare handoff | `POST /api/v0/cases/{case_id}/handoff/prepare` | creates editable package, does not submit |
| Confirm handoff | `POST /api/v0/cases/{case_id}/handoff/confirm` | durable outbox submission request |
| Retry failed handoff | `POST /api/v0/cases/{case_id}/handoff/retry` | same logical handoff/idempotency semantics |
| Complete case | `POST /api/v0/cases/{case_id}/complete` | user-initiated completion (§9) |
| Submit feedback | `POST /api/v0/cases/{case_id}/feedback` | stores user feedback after completion; once per case |
| Notifications catch-up | `GET /api/v0/notifications?after=` | owner-scoped, monotonic `notification_id` (§12) |
| Notifications stream | `GET /api/v0/notifications/stream` | owner-level SSE, independent of any case (§12) |
| Acknowledge notifications | `POST /api/v0/notifications/ack` | marks `notification_id`s as read; idempotent |
| Support status webhook | `POST /api/v0/integrations/support/status` | inbound, HMAC-signed, disabled by default (§14); not for browsers |
| Read quality/issues | `/api/v0/analytics/...` | authz proxy/read model only |

State-changing calls accept `Idempotency-Key` where browser retry is plausible.

## 4. Core response shapes

### 4.1. `CaseSnapshot`

Conceptual JSON:

```json
{
  "case_id": "...",
  "conversation_status": "ACTIVE",
  "resolution_status": "UNKNOWN",
  "last_decision": "ANSWER",
  "moderation_warning_count": 0,
  "active_turn": {
    "turn_id": "...",
    "revision": 3,
    "status": "COMPLETED"
  },
  "handoff": null,
  "timeline": [],
  "last_event_id": "42"
}
```

Additional snapshot fields:

```json
{
  "completed_at": null,
  "completion_reason": null,
  "feedback": null
}
```

`completion_reason ∈ USER | SUPPORT | MODERATION | null`; `feedback` is the stored feedback object (§9) or `null`.

Rules:

- `ANSWER` never implies `RESOLVED`.
- `moderation_warning_count` is case-scoped server state, not local UI counter.
- archived/read-only presentation is `conversation_status != ACTIVE`; do not invent a fifth lifecycle enum solely for the sidebar.

### 4.2. `TimelineItem`

Every visible item has stable identity and server provenance:

```json
{
  "item_id": "...",
  "type": "AI_ANSWER",
  "occurred_at": "...",
  "turn_id": "...",
  "payload": {}
}
```

Minimum item/event types:

- `USER_MESSAGE`
- `TURN_STAGE`
- `AI_ANSWER`
- `CLARIFICATION`
- `NO_CONFIRMED_ANSWER`
- `MODERATION_WARNING`
- `CONVERSATION_CLOSED`
- `HANDOFF_OFFER`
- `HANDOFF_STATUS`
- `CASE_RESOLUTION_CHANGED`
- `CASE_COMPLETED`
- `TECHNICAL_ERROR`
- `FEEDBACK_REQUESTED`
- `FEEDBACK_SUBMITTED`

`HANDOFF_STATUS` payload is the handoff view (§4.4) plus `changed: ["status" | "stage" | "assigned_specialist" | "terminal"]` so the UI can highlight what moved.

`CASE_COMPLETED` payload: `{ "completion_reason": "USER | SUPPORT | MODERATION", "resolution_status": "..." }`.

UI may combine events into richer widgets, but must not change their meaning.

### 4.3. Answer/source payload

```json
{
  "markdown": "...",
  "sources": [
    {
      "fragment_id": "...",
      "title": "...",
      "page": 17,
      "label": "Открыть источник"
    }
  ]
}
```

A source button appears only when `fragment_id` is persisted for the published answer. Browser opens it through API proxy, never through a fabricated URL.

### 4.4. Handoff view

```json
{
  "status": "ACCEPTED",
  "integration_mode": "REAL",
  "external_case_id": "...",
  "assigned_specialist": {
    "ref": "opaque-id",
    "display_name": "Иван Петров"
  },
  "stage": {
    "code": "IN_PROGRESS",
    "display_name": "В работе"
  },
  "terminal": null,
  "stale": false,
  "updated_at": "..."
}
```

All fields after `status`/`integration_mode` are optional. If adapter did not confirm specialist/stage, API returns `null` and UI hides the corresponding row — never a placeholder like «уточняется». `SIMULATED_ACCEPTED` must be visibly marked as demo/simulated, including the specialist row. `stale: true` means status polling reached its TTL without a terminal fact; UI shows «статус не обновляется» and keeps the last known stage.

## 5. Message command

Conceptual request:

```json
{
  "text": "...",
  "client_message_id": "uuid"
}
```

Accepted response:

```json
{
  "case_id": "...",
  "turn_id": "...",
  "revision": 4,
  "status": "QUEUED"
}
```

The browser may optimistically render the user bubble, but final turn/timeline identity comes from server response/event.

Validation: `text` is required (non-blank) and at most 4000 characters; a longer body is a `400` validation error (`errors.text`), never truncated server-side. A concurrent second send for the same case while the first one is still being persisted is a `409 CONCURRENCY_CONFLICT` — the client reloads the snapshot and resends.

## 6. SSE/event contract

Envelope:

```text
id: <monotonic event id within case>
event: case_event
data: { event_id, case_id, turn_id?, revision?, type, occurred_at, payload }
```

Rules:

- ordering is authoritative by `event_id` within a case;
- reconnect uses `Last-Event-ID` or `after=` catch-up;
- duplicate delivery is tolerated by `event_id` dedupe;
- stale/superseded turn events may be persisted for trace but cannot publish a new authoritative answer;
- connection loss never changes domain state.

## 7. Moderation UX

`MODERATION_WARNING` payload contains only observable policy data, e.g. warning count and user-facing message. No classifier probability/chain-of-thought.

`MODERATION_CLOSE`/`CONVERSATION_CLOSED` makes the current chat read-only for new user messages. It does not ban the account, delete history or revoke an already accepted handoff.

Active close threshold is a server-side Domain policy. Frontend never decides whether a violation is first/second.

## 8. Handoff commands

`prepare` returns an editable package preview. It does **not** set `HandoffStatus=PENDING`.

`confirm` persists handoff + outbox atomically and then sets `PENDING`. `ACCEPTED`/`SIMULATED_ACCEPTED` appears only after adapter acknowledgement.

A failed transport keeps the prepared/persisted context and exposes retry without pretending success.

## 9. Completion and feedback

A case completes through exactly one of the paths in `architecture.md §5.8`. From the browser side:

### 9.1. User-initiated completion

`POST /api/v0/cases/{case_id}/complete`

```json
{ "solved": true }
```

`solved ∈ true | false | null`. Response: updated `CaseSnapshot`. Effects: `conversation_status = CLOSED_USER`, `resolution_status = RESOLVED | UNRESOLVED | UNKNOWN`, events `CASE_RESOLUTION_CHANGED` (if changed), `CASE_COMPLETED`, `FEEDBACK_REQUESTED`, notification `CASE_COMPLETED`. `409 CASE_CLOSED` if already completed.

### 9.2. Support-initiated completion

Arrives as `HANDOFF_STATUS` with `terminal != null`, followed by the same `CASE_COMPLETED` / `FEEDBACK_REQUESTED` events; `completion_reason = SUPPORT`. The browser does nothing special — it renders the events.

### 9.3. Feedback widget

Rendered on `FEEDBACK_REQUESTED` (never after `CLOSED_MODERATION`) and on reload when `completed_at != null && feedback == null`. Request:

```json
{
  "specialist_rating": "POSITIVE | NEGATIVE | null",
  "information_quality_rating": "POSITIVE | NEGATIVE | null",
  "solved": true,
  "comment_text": "optional"
}
```

Rules:

- one feedback per case; a second submit returns `409`, the same `Idempotency-Key` returns the stored one;
- `specialist_rating` control is hidden unless `handoff.status ∈ {ACCEPTED, SIMULATED_ACCEPTED}`; with `SIMULATED_ACCEPTED` it is labelled demo;
- `solved` control is hidden when `resolution_status != UNKNOWN`; when sent, it updates `resolution_status` only from `UNKNOWN`;
- information-quality feedback is not equivalent to evaluator factual quality;
- positive feedback does not set `RESOLVED`;
- negative feedback is not evidence of employee fault;
- API owns feedback persistence, emits `FEEDBACK_SUBMITTED`; analytics receives a copy asynchronously.

## 10. Error semantics

Public errors use stable machine `code` + safe user message. At minimum distinguish:

- validation/authz/not-found/conflict;
- `KNOWLEDGE_UNAVAILABLE` / `KNOWLEDGE_TIMEOUT`;
- `HANDOFF_FAILED`;
- `CASE_CLOSED` (message/complete on a completed case);
- `FEEDBACK_ALREADY_SUBMITTED`;
- idempotency conflict.

The UI must never render knowledge infrastructure failure as «в базе нет информации».

## 11. Security

- case/source/analytics/notification access is authorized server-side by `owner_id` (§13);
- raw Markdown and any adapter-supplied display text (stage, specialist) are sanitized before render;
- no internal service URL is returned to browser;
- no chain-of-thought/raw private prompt in payloads;
- demo integration state is server-controlled, not selected by arbitrary client query parameter.

## 12. Notifications

Owner-scoped, independent of which case is open.

```json
{
  "notification_id": "1042",
  "case_id": "...",
  "type": "HANDOFF_UPDATED | CASE_COMPLETED | FEEDBACK_REQUESTED",
  "occurred_at": "...",
  "read_at": null,
  "payload": { "title": "...", "body": "...", "integration_mode": "REAL | SIMULATED | null" }
}
```

- `GET /api/v0/notifications?after=<notification_id>&unread=true` — catch-up; `notification_id` is monotonic per owner;
- `GET /api/v0/notifications/stream` — SSE envelope `event: notification`, `id: <notification_id>`, reconnect via `Last-Event-ID`;
- `POST /api/v0/notifications/ack { "ids": [...] }` — idempotent.

Browser behaviour:

1. stream connected, tab visible → toast + badge;
2. stream connected, `document.visibilityState == "hidden"` and `Notification.permission == "granted"` → `new Notification(title, { body, tag: notification_id })`; clicking focuses the tab and opens the case;
3. no stream (browser was closed) → on next load, `GET /api/v0/notifications?unread=true` feeds the badge on the case list.

Permission for OS notifications is requested once, after the first handoff confirm — not on page load. Web Push (service-worker push from an external push service) is **not** part of this contract (ADR-0002).

`payload.title/body` are server-composed user-facing strings; simulated facts say so in the text.

## 13. Owner session

`api` issues an opaque `owner_id` in an `HttpOnly; SameSite=Lax; Secure` cookie on `POST /api/v0/session` (also implicitly on the first `POST /api/v0/cases` without a cookie). Every case, notification and feedback row stores `owner_id`; every read/write checks it. There are no accounts, logins or passwords in P0; clearing cookies makes previous cases unreachable, which is acceptable for the hackathon and stated in the UI.

The cookie is the only credential the browser holds. `case_id` alone never grants access.

## 14. Support status webhook (not for browsers)

`POST /api/v0/integrations/support/status` — inbound channel for a real support adapter; semantics, signature and idempotency in `support-adapter-v0.md §6.2`. It is registered only when `Support:Webhook:Enabled=true`, requires `X-Support-Signature` and `X-Support-Timestamp`, and never reads the owner cookie. It exists in this document only so the public surface list is complete.
