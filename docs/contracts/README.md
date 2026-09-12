# Contracts registry

`docs/contracts/` содержит нормативные границы между независимо разрабатываемыми блоками. Здесь фиксируется **семантика**, которой должны следовать обе стороны. Phase labels (`G0/G1/G2`) относятся к историческому execution plan и не определяют текущий статус контракта.

## 1. Реестр

| Contract | Status | Producer/owner | Consumer | Machine-readable artifact |
|---|---|---|---|---|
| `knowledge-v0` | **frozen v0** | Python `knowledge` | `.NET api` / `api-worker` | `knowledge-v0.openapi.yaml` |
| `web-api-v0` | **frozen v0 semantics** | `.NET api` | React `web` | ASP.NET/OpenAPI when exported; this Markdown remains normative for semantics |
| `support-adapter-v0` | **frozen v0 semantics** | `.NET Application` port | Infrastructure/demo/real adapter | C# interface + contract tests |

Database ownership/state invariants are not separate transport contracts; their authority is `docs/architecture.md`.

## 2. Change policy

- Breaking = remove/rename required field, change type, change existing enum meaning, change acknowledgement/idempotency semantics.
- Breaking transport change creates a new version (`v1`) and migrates both sides in one coordinated change.
- Additive optional fields are allowed in `v0` if old consumers remain safe.
- A generated client/schema is never the Domain model. Map at the boundary.
- Shared contract edits are integration-owner work; do not let two subagents independently edit the same contract.
- Any contract change needs at least one consumer/producer test fixture demonstrating compatibility.
- Product-enhancement work in `product-experience.md` does **not** implicitly extend a frozen contract. If a feature needs new fields/events/routes, update the relevant contract and tests in the same change.

## 3. Error rule shared by all boundaries

Differentiate business/knowledge outcomes from infrastructure failure.

Examples:

- `evidence_sufficiency=INSUFFICIENT` is a valid knowledge result;
- timeout/5xx/schema-invalid body is infrastructure failure;
- adapter `REJECTED` is a handoff transport/business acknowledgement result;
- missing required request field is validation failure.

No boundary may convert transport failure into a plausible domain success.

## 4. Trace/idempotency conventions

Where applicable:

- `X-Trace-Id` propagates cross-runtime;
- state-changing public API commands use `Idempotency-Key`;
- outbox/worker delivery is at-least-once and consumer effects are idempotent by business key;
- do not claim exactly-once semantics.

## 5. Contract verification requirements

These checks are ongoing compatibility gates, not future scaffold TODOs. Report only checks that actually ran.

### knowledge-v0

- committed OpenAPI and FastAPI routes remain compatible; exact generated-schema equality may be claimed only after the export comparison actually runs;
- generated/typed C# boundary client deserializes and maps into Application records;
- fixture responses deserialize and map into Application records;
- forbidden decision-like response keys cause `INVALID_RESPONSE`;
- timeout/5xx/model-error mappings are distinct.

### web-api-v0

- API surface/snapshots include all fields used by UI;
- SSE event replay/reconnect by event id (case stream and owner notification stream);
- same idempotency key + same payload returns same logical result;
- reload from snapshot reproduces timeline state, incl. completed/archived cases;
- `complete` → `CASE_COMPLETED` + `FEEDBACK_REQUESTED` + notification row; second `complete` → conflict according to the public error contract;
- feedback once per case; four signals stored independently;
- notifications catch-up by `notification_id`, ack idempotent;
- owner cookie mismatch → no access; `case_id` alone grants nothing;
- source/handoff/feedback/notification actions are authz checked server-side.

### support-adapter-v0

- stable idempotency key on retry;
- accepted only after positive adapter acknowledgement;
- unknown specialist/stage stays null, never fabricated;
- simulated mode is explicit in persisted state/UI;
- timeout/failure is retryable without losing prepared package;
- status snapshot idempotent by `(handoff_id, external_revision)`; poll and webhook converge;
- terminal status completes the case exactly once (`CLOSED_SUPPORT` where applicable);
- `staged` demo script deterministic per `handoff_id`;
- webhook rejects unsigned / stale / mismatched requests and is absent when disabled.

### knowledge-v0 quality pushes

- `QualityFeedbackPush` with the four signals + `specialist_ref` deserializes; deprecated compatibility fields are not treated as new product truth;
- `QualityCompletionPush` idempotent by `case_id`;
- analytics fixtures can be built from turns + feedback + completions alone (no API DB).

## 6. Current contract status

The committed `knowledge-v0.openapi.yaml` remains the frozen machine-readable artifact for the `.NET ↔ knowledge` boundary. Real retrieval/answerability/draft/verify implementations coexist with deterministic fixture/stub paths used for tests; **stub mode is no longer a statement about implementation maturity**.

Do not claim byte-for-byte FastAPI export parity, clean NSwag regeneration, or complete browser/OpenAPI parity unless the corresponding check has actually been executed on the current commit. A missing verification is a verification gap, not a reason to describe the whole repository as a scaffold.
