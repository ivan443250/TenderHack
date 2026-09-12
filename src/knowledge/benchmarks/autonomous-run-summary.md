# TenderHack autonomous run summary

> **Historical benchmark/run artifact.** This file records the pre-ADR-0003 K2–K6 implementation/run and must not be read as the current Model Stack v2 certification status. In particular, its vLLM/model-runtime notes are superseded by `docs/adr/0003-model-stack-v2.md` and the active `docs/plans/active/model-stack-v2-real-certification.md`. The measurements below remain valid only for the commit/config they originally described.

## Local commit stack

- `b2af8ab` — K2 retrieval and review foundation
- `3de1292` — `feat(knowledge): add verified condition cards`
- `df1c1c6` — `feat(knowledge): add evidence answerability gate`
- current stage at the time — `feat(knowledge): add grounded drafting and verification` (local checkpoint commit)

No push, pull, rebase, reset, or remote synchronization was performed during that historical autonomous run.

## Evidence pipeline status at that run

- K2 retrieval: `PASS_WITH_LIMITATIONS`; measured Recall@10 `0.75` (target `0.90`), exact-code hit `0.6875`; normative snapshot `snap_0e979dfef376faff41f7c76416fda457`.
- K3 condition cards: `PASS`; 20 card identities / 22 immutable versions, all audited against normative fragments.
- K4 answerability: `PASS_WITH_LIMITATIONS`; 35 deterministic fixture cases, no false `SUFFICIENT` result in the real-PostgreSQL check.
- K5/K6 draft and verification: `PASS_WITH_LIMITATIONS`; grounded JSON draft, deterministic claim postchecks, and frozen API projection were implemented.

## K5/K6 implementation

`draft_v1` treats user text and supplied evidence as data and requires fragment citations. `verify_v1` postchecks source membership, lexical grounding, numbers, exact codes, statuses, button/action labels, URLs, and applicability conditions. A draft is generated only after the K4 gate returns `SUFFICIENT`; insufficient or condition-dependent evidence returns an empty gated draft. Invalid model output or unsupported claims fail closed with `MODEL_ERROR`; an unreachable local runtime maps to `MODEL_UNAVAILABLE`.

The real PostgreSQL integration exercised K4 fixture fragments and wrong-snapshot rejection. Normal generation tests used a fake generator. Real vLLM smoke was `BLOCKED_ENVIRONMENT` on that Windows host; no model download was attempted. This is historical evidence only and is not the current llama.cpp/Giga/Qwen3.8 certification result.

## Remaining blockers recorded at that run

- Retrieval coverage was below the then-current Recall@10 target, so answer quality was bounded by candidate recall.
- Real Linux inference/performance measurement required a team-controlled runtime.
- Organizer/domain questions were still open.

## Historical recommended next action

The run recommended a controlled Linux model smoke and reviewed claim-level evaluation. The current successor action is the Model Stack v2 certification plan referenced above. The invariant remains unchanged: generated text is not a Support Core decision; `.NET` is the sole owner of business decisions and state.
