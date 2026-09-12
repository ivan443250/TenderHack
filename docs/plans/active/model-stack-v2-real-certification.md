# Model Stack v2 — real certification runbook

Status: **TARGET / TO BE CERTIFIED**. This document is the next-task runbook,
not evidence that a model, profile or Cloud.ru deployment has already passed.
It must be executed on a disposable environment and recorded in
`src/knowledge/benchmarks/model-stack-v2-real-certification.json`.

## Guardrails and acceptance criteria

- Do not change product decisions, state ownership, HTTP contracts, migrations
  or deployment topology as part of certification.
- Do not use an external LLM or search API. Provisioning downloads are allowed
  only before the offline gate; runtime must work from local artifacts.
- Keep `.NET` as the sole owner of business decisions/state. Python returns
  evidence, scores, drafts and verification facts only.
- Keep normative six-manual retrieval separate from historical support data.
- Stop on corpus identity drift, missing provenance, mixed embedding spaces,
  fabricated metrics, or an unverified artifact being treated as certified.
- A component ends as exactly one of `CERTIFIED`,
  `CERTIFIED_WITH_LIMITATIONS`, `BLOCKED_ENVIRONMENT`, or `UNVERIFIED`.

Capability levels are runtime states, not product decisions:

| Level | Available capabilities | Safe behavior when unavailable |
|---|---|---|
| `FULL` | embedding + reranker + generator | normal evidence-gated pipeline |
| `REDUCED` | embedding + generator; reranker unavailable | exact + FTS + `pg_trgm` + Giga dense + RRF |
| `MINIMAL` | exact/FTS retrieval; model functions unavailable | grounded/extractive response, clarify or handoff; never hallucinate |

The A100 is an accelerator profile, never a product requirement. The portable
profile must remain functionally valid with reduced capabilities.

## Profile targets

### `cloud-a100`

Target: one team-controlled Cloud.ru Docker RUN deployment on a Linux host with
an A100 80 GB GPU. Prefer canonical/BF16 model weights only where runtime
compatibility is measured. The intended logical stack is Giga embeddings,
Querit reranking and Qwen3.8 generation. Record all results; do not assume
latency, memory, compatibility or simultaneous residency in advance.

Status before this run: `TARGET_NOT_CERTIFIED`.

### `portable`

Target: materially weaker hardware. Use the pinned Giga Q8_0 GGUF embedding
artifact and Qwen3.8 Q6_K GGUF generator artifact. Querit is optional; if its
portable artifact/interface is not verified, set it to `DISABLED` and use the
safe retrieval fallback. A missing embedding keeps exact + FTS + `pg_trgm`;
a missing generator emits no generated answer and follows existing grounded or
handoff behavior.

Status before this run: `TARGET_NOT_CERTIFIED`.

## Phase 0 — environment inventory

Record before loading any model:

- CPU model and cores, RAM, GPU model and VRAM;
- driver/CUDA versions, OS/kernel, Docker and Compose versions;
- free disk space and filesystem type;
- Python/uv versions and the exact Knowledge image/runtime digest.

For `cloud-a100`, additionally record the Cloud.ru instance/GPU type,
container image digest and external model mount paths. For `portable`, record
the host and GPU constraints that explain any reduced capability. Save the
inventory in the final report; do not infer missing values.

## Phase 1 — model artifact verification

For each role, record source model, pinned revision, runtime artifact/revision,
hash, precision/quantization, license and serving runtime. A successful
download is not certification.

Current expected records:

| Role | Source | Runtime artifact | Runtime status |
|---|---|---|---|
| Embedding | `ai-sage/Giga-Embeddings-instruct-3B-0826` | `ai-babai/giga-embeddings-0826-3b-gguf/giga-embeddings-0826-3b-q8_0.gguf`, Q8_0 | pinned and hash-recorded; verify locally |
| Reranker | `Querit/Querit-4B` | canonical/BF16 on `cloud-a100` if feasible; portable quantization TBD | no portable artifact is certified yet |
| Generator | `empero-ai/Qwen3.8-4B-Distill` | `empero-ai/Qwen3.8-4B-Distill-GGUF/Qwen3.8-4B-Q6_K.gguf` | local llama.cpp; exact hash/smoke pending |

Do not invent a Querit filename, hash or size. Keep source license separate from
runtime-artifact provenance; the Giga GGUF is a third-party conversion.

## Phase 2 — Giga embedding smoke

Use a Russian support query and Russian normative passages from the six-manual
corpus. Verify:

1. every output has exactly 2048 finite values;
2. vectors are normalized according to the adapter contract;
3. `giga_portal_support_v1` is applied to queries only;
4. document embedding has no query instruction;
5. basic semantic ordering is sensible and deterministic across repeat calls.

Record request/response metadata without raw user text in default logs. Assign
the result one of the four certification statuses.

## Phase 3 — clean database and corpus identity

Create a disposable PostgreSQL 16 + pgvector database, apply the current
Alembic head, and ingest only the six approved normative PDFs. Verify the stable
identity before backfill:

- 6 documents;
- 830 pages;
- 3899 fragments;
- deterministic document/version/fragment identities and source anchors.

If any count or identity differs, stop and explain the corpus difference. Do
not silently benchmark a different extraction or source set. Historical support
data must not enter the normative snapshot.

## Phase 4 — real embedding backfill

After Phase 3 passes, create exactly 3899 active embeddings. Verify dimension,
model metadata, snapshot membership, no stale vectors, no orphan rows,
restart/readback durability and source-button provenance.

Every vector row and query must carry a conceptual `embedding_space_id` before
production backfill. It must include at least:

- source model and source revision;
- runtime artifact/precision;
- dimension;
- query-instruction version.

Use distinct IDs such as `giga-0826-bf16-v1` and `giga-0826-q8_0-v1`. Do not
silently mix canonical/BF16 document vectors with portable Q8_0 query vectors;
measure interchangeability first. This is a **HIGH certification requirement**
and is intentionally not a database-schema change in this preparation task.

## Phase 5 — retrieval evaluation

Run the unchanged 44-query gold set against the same corpus and report each
configuration separately:

- **B1:** exact + PostgreSQL FTS + `pg_trgm` baseline;
- **B2:** B1 + Giga dense + RRF;
- **B3:** B2 + Querit, only if Querit runtime is certified in Phase 6.

Measure Recall@5, Recall@10, MRR, typo subset and exact-code subset. Record
p50, p95 and mean latency. `Recall@10 >= 0.90` is a target, not a guaranteed
result. Report misses exactly; never blend hand-crafted adversarial cases into
the unchanged gold metric.

## Phase 6 — Querit certification

On `cloud-a100`, prefer the canonical/source model if its runtime is feasible.
Verify query-passage serialization, the custom model interface, score
extraction, Russian ranking behavior, memory and latency. Scores are relevance
evidence, not probabilities of correctness.

For `portable`, certify a quantized Querit path only when the exact artifact,
hash and runtime interface are verified. Otherwise record
`portable_reranker = DISABLED` and keep the B2 fallback.

## Phase 7 — Qwen3.8 generation certification

Verify the local llama.cpp chat-completions endpoint with Russian prompts and
grounded evidence. Test structured JSON, claim lists, fragment IDs, malformed
output, `MODEL_UNAVAILABLE` mapping and `<think>` non-exposure. Existing
deterministic verification remains mandatory; generated text never becomes a
Support Core decision.

## Phase 8 — full pipeline

Exercise the unchanged pipeline:

`retrieval → answerability → draft → verify`

Cover answerable, condition-dependent, insufficient-evidence, exact
code/status, typo, human-required, generator-unavailable,
reranker-unavailable and embedding-unavailable cases. Confirm that no model
profile changes `.NET` decisions, state transitions, moderation, routing,
handoff, feedback or source provenance.

## Phase 9 — resource benchmark

For each available profile record real VRAM, RAM, embedding latency, reranker
latency, TTFT where applicable, generation tokens/second and end-to-end
latency at 1, 2 and 4 concurrent requests. Compare profiles only with their
hardware clearly labelled. Do not claim all three models fit simultaneously on
portable hardware without measurement.

## Phase 10 — offline / no external AI

After artifacts are provisioned locally, disable downloads and external AI
access. Restart from local mounts and verify inference, retrieval and fallback
behavior. Provisioning from Hugging Face is distinct from runtime inference;
the demo runtime must not depend on remote AI calls.

## Cloud.ru Docker RUN deployment note

This is a target checklist, not a tested deployment:

- Linux container and GPU-backed inference on one team-controlled Docker host;
- model artifacts mounted externally or from controlled object storage;
- inference endpoints reachable only by Knowledge, with authentication for
  non-local deployment;
- no browser-to-inference traffic and no raw request logging by default;
- health/readiness endpoints are required;
- use a warm demo instance; do not intentionally scale to zero.

Exact Cloud.ru instance, image and storage values are selected and measured in
Phase 0. Status: **TARGET / TO BE CERTIFIED**.

## Hackathon requirement alignment

The certification evidence must demonstrate local/team-controlled intelligent
search; no external LLM/search API; relevant answers from structured KB;
safe no-answer and handoff; reproducible Linux/web deployment; a constrained
hardware fallback; no unsupported hallucinated answer; retained sources and
provenance; and a scalability/resource record. These checks do not change
product behavior or ownership boundaries.

## Organizer checkpoint

The exact self-hosted inference permission question is recorded once in
`src/knowledge/benchmarks/organizer-questions.md`. Until answered, assume
team-controlled deployment of permitted open models is acceptable for
preparation, but do not claim Cloud.ru production certification.

## Final certification report shape

Write `src/knowledge/benchmarks/model-stack-v2-real-certification.json` with:

`environment`, `model_artifacts`, `embedding_space`, `corpus_identity`,
`backfill`, `B1`, `B2`, `B3`, `generator`, `pipeline`, `latency`, `memory`,
`offline`, `fallbacks`, `limitations`, `high_findings`, `medium_findings`,
and `recommended_demo_profile`.

The recommended profile must be derived from measured evidence. If a gate is
blocked, preserve the blocker and use the safest certified fallback; never
turn a failure into `PASS`.
