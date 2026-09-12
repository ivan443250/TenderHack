# TenderHack autonomous run summary

## Local commit stack

- `b2af8ab` — K2 retrieval and review foundation
- `3de1292` — `feat(knowledge): add verified condition cards`
- `df1c1c6` — `feat(knowledge): add evidence answerability gate`
- current stage — `feat(knowledge): add grounded drafting and verification` (local checkpoint commit)

No push, pull, rebase, reset, or remote synchronization was performed.

## Evidence pipeline status

- K2 retrieval: `PASS_WITH_LIMITATIONS`; measured Recall@10 `0.75` (target `0.90`), exact-code hit `0.6875`; normative snapshot `snap_0e979dfef376faff41f7c76416fda457`.
- K3 condition cards: `PASS`; 20 card identities / 22 immutable versions, all audited against normative fragments.
- K4 answerability: `PASS_WITH_LIMITATIONS`; 35 deterministic fixture cases, no false `SUFFICIENT` result in the real-PostgreSQL check.
- K5/K6 draft and verification: `PASS_WITH_LIMITATIONS`; grounded JSON draft, deterministic claim postchecks, and frozen API projection are implemented.

## K5/K6 implementation

`draft_v1` treats user text and supplied evidence as data and requires fragment citations. `verify_v1` postchecks source membership, lexical grounding, numbers, exact codes, statuses, button/action labels, URLs, and applicability conditions. A draft is generated only after the K4 gate returns `SUFFICIENT`; insufficient or condition-dependent evidence returns an empty gated draft. Invalid model output or unsupported claims fail closed with `MODEL_ERROR`; an unreachable local runtime maps to `MODEL_UNAVAILABLE`.

The real PostgreSQL integration exercises K4 fixture fragments and wrong-snapshot rejection. Normal generation tests use a fake generator. Real vLLM smoke is `BLOCKED_ENVIRONMENT` because this Windows host has no reachable Linux-target runtime; no model download was attempted.

## Remaining blockers and questions

- Retrieval coverage remains below the Recall@10 target, so answer quality is bounded by candidate recall.
- Real vLLM/Linux smoke and performance measurement require a team-controlled Linux inference runtime.
- Existing organizer questions remain in `organizer-questions.md`; no new unsupported business rule was invented in K5/K6.

## Recommended next action

Run a controlled Linux vLLM smoke with the pinned generator revision, then evaluate grounded drafts on a reviewed claim-level set. Do not treat generated text as a Support Core decision; `.NET` remains the sole owner of business decisions and state.
