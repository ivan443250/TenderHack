# Browser ↔ API contract v0

Статус: **semantic freeze for G0/G1**. До появления `.NET` scaffold это нормативная семантика публичной browser boundary. После scaffold ASP.NET OpenAPI становится machine-readable artifact и должен оставаться совместимым с этим документом.

Browser ходит **только** в `api`. Он не знает адреса `knowledge`, PostgreSQL, inference runtime или support adapter.

## 1. Главная модель UI

99% пользовательского UX — один server-driven chat timeline:

```text
UserMessage
→ Progress / ModerationWarning / Clarification / AiAnswer
→ SourceLink(s)
→ HandoffOffer
→ HandoffStatus
→ CaseCompleted
→ FeedbackWidget
→ read-only/archive projection
```

Frontend не выводит lifecycle state из анимации/текста ответа. Authoritative state приходит из `CaseSnapshot` + `case_events`/SSE.

## 2. State vocabulary

Authority: `docs/architecture.md §6` / Domain enums.

```text
ConversationStatus = ACTIVE | CLOSED_USER | CLOSED_MODERATION
ResolutionStatus   = UNKNOWN | RESOLVED | UNRESOLVED
TurnStatus         = QUEUED | RUNNING | COMPLETED | FAILED | SUPERSEDED
HandoffStatus      = NOT_REQUESTED | PENDING | ACCEPTED | SIMULATED_ACCEPTED | FAILED
Decision           = ANSWER | CLARIFY | HANDOFF_OFFER | ANSWER_AND_HANDOFF |
                     MODERATION_WARNING | MODERATION_CLOSE | TECHNICAL_ERROR
```

`MODERATION_WARNING` is supported by the UI/event contract so the team-requested warning-first policy can be enabled without a frontend breaking change. Whether the active demo policy closes on first or second confirmed violation is tracked in `docs/open-decisions.md` until organizer confirmation.

## 3. Public endpoint surface

Exact route names may be adjusted once Minimal API is scaffolded, but these capabilities and semantics are frozen.

| Capability | Suggested route | Semantics |
|---|---|---|
| Create case | `POST /api/v0/cases` | creates/restores one chat case; idempotent |
| Read case snapshot | `GET /api/v0/cases/{case_id}` | full server state for reload/recovery |
| Send user message | `POST /api/v0/cases/{case_id}/messages` | persists user input and starts new turn/revision |
| Event catch-up | `GET /api/v0/cases/{case_id}/events?after=` | monotonic event replay/poll fallback |
| Event stream | `GET /api/v0/cases/{case_id}/events/stream` | SSE, supports reconnect via event id |
| Resolve source | `GET /api/v0/sources/{fragment_id}` | authz proxy to knowledge source endpoint |
| Prepare handoff | `POST /api/v0/cases/{case_id}/handoff/prepare` | creates editable package, does not submit |
| Confirm handoff | `POST /api/v0/cases/{case_id}/handoff/confirm` | durable outbox submission request |
| Retry failed handoff | `POST /api/v0/cases/{case_id}/handoff/retry` | same logical handoff/idempotency semantics |
| Submit feedback | `POST /api/v0/cases/{case_id}/feedback` | stores user feedback after completion/when allowed |
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

Rules:

- `ANSWER` never implies `RESOLVED`.
- `moderation_warning_count` is case-scoped server state, not local UI counter.
- archived/read-only presentation is derived from authoritative case/resolution policy; do not invent a fifth lifecycle enum solely for the sidebar.

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
- `TECHNICAL_ERROR`
- `FEEDBACK_REQUESTED`

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
  "updated_at": "..."
}
```

All fields after `status`/`integration_mode` are optional. If adapter did not confirm specialist/stage, API returns `null` and UI hides the corresponding row. `SIMULATED_ACCEPTED` must be visibly marked as demo/simulated.

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

When authoritative support/case logic marks resolution, API emits `CASE_RESOLUTION_CHANGED`. Web shows completion notification and feedback widget.

Feedback request supports four independent signals:

```json
{
  "specialist_rating": "POSITIVE | NEGATIVE | null",
  "information_quality_rating": "POSITIVE | NEGATIVE | null",
  "solved": true,
  "comment_text": "optional"
}
```

Rules:

- `specialist_rating` is nullable/hidden when no human specialist actually participated;
- information-quality feedback is not equivalent to evaluator factual quality;
- positive feedback does not set `RESOLVED`;
- negative feedback is not evidence of employee fault;
- API owns feedback persistence; analytics receives a copy asynchronously.

## 10. Error semantics

Public errors use stable machine `code` + safe user message. At minimum distinguish:

- validation/authz/not-found/conflict;
- `KNOWLEDGE_UNAVAILABLE` / `KNOWLEDGE_TIMEOUT`;
- `HANDOFF_FAILED`;
- `CASE_CLOSED`;
- idempotency conflict.

The UI must never render knowledge infrastructure failure as «в базе нет информации».

## 11. Security

- case/source/analytics access is authorized server-side;
- raw Markdown is sanitized before render;
- no internal service URL is returned to browser;
- no chain-of-thought/raw private prompt in payloads;
- demo integration state is server-controlled, not selected by arbitrary client query parameter.
