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

`llama.cpp` / `llama-server` is the primary serving runtime family for verified
GGUF artifacts on both profiles. The same artifact should run from CPU-class
hardware to A100 where practical; hardware changes backend, GPU offload,
batching, parallelism, KV-cache mode and threads. This is a reproducibility and
portability objective, not a claim of maximum theoretical A100 throughput.
vLLM is outside the critical path.

## Profile targets

### `cloud-a100`

Target: one team-controlled Cloud.ru Docker RUN deployment on a Linux host with
an A100 80 GB GPU. Use the same verified Giga Q8_0 and Qwen3.8 Q6_K GGUF
artifacts as the portable profile and target full CUDA offload. The intended
logical stack is Giga embeddings, Querit reranking and Qwen3.8 generation;
Querit is enabled only after its runtime path is certified. Record all results;
do not assume latency, memory, compatibility or simultaneous residency in
advance. Any BF16 path is an optional future experiment outside the critical
path, not the default A100 plan.

Status before this run: `TARGET_NOT_CERTIFIED`.

### `portable`

Target: materially weaker hardware using the same pinned Giga Q8_0 GGUF
embedding artifact and Qwen3.8 Q6_K GGUF generator artifact. At process
startup, select full GPU offload when the model fits, partial GPU/CPU offload
when needed, or CPU execution. Querit is optional; if its portable
artifact/interface is not verified, set it to `DISABLED` and use the safe
retrieval fallback. A missing embedding keeps exact + FTS + `pg_trgm`; a
missing generator emits no generated answer and follows existing grounded or
handoff behavior. Do not migrate model placement CPU↔GPU per request.

Status before this run: `TARGET_NOT_CERTIFIED`.

## Runtime, residency and readiness policy

For verified GGUF artifacts, `llama-server` processes are persistent. Models are
loaded once during startup, remain resident while enabled and certified, and
are not loaded per request or evicted by an LRU during normal operation. On
`cloud-a100`, Giga and Qwen3.8 are expected to remain resident; Querit joins
only after its runtime path is certified. This does not guarantee that all
three models fit on every host.

The inference topology is logical rather than a machine requirement:

```text
Knowledge
  └─ provider layer / internal gateway
       ├─ embedding provider → persistent llama-server
       ├─ reranker provider  → certified llama-server, local Querit provider, or disabled
       └─ generator provider → persistent llama-server
```

The browser never calls inference directly. A service is process-alive when its
socket is listening, but it is capability-ready only after a bounded smoke of
every enabled provider: a valid 2048-vector embedding, a valid reranker score
when enabled, and a valid structured chat response. A disabled Querit provider
must not make the REDUCED profile unhealthy.

For embedding and generator servers, verify the exact pinned llama.cpp version
before freezing command flags. Embedding mode, batch input and OpenAI-compatible
chat completions are requirements to test, not assumed flags. Continuous
batching is a desired generator capability; record whether the exact build
supports it and do not fabricate its impact.

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
| Reranker | `Querit/Querit-4B` | verified Querit GGUF + llama-server, canonical source through a separate local provider, or disabled | no portable artifact or llama.cpp path is certified yet |
| Generator | `empero-ai/Qwen3.8-4B-Distill` | `empero-ai/Qwen3.8-4B-Distill-GGUF/Qwen3.8-4B-Q6_K.gguf` | local llama.cpp; exact hash/smoke pending |

Do not invent a Querit filename, hash or size, and do not claim llama.cpp can
serve Querit until that path is proven. Keep source license separate from
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

Do not issue one HTTP request per fragment unless a measured runtime limitation
forces it. Benchmark practical embedding batch sizes `8`, `16`, `32` and `64`
(or a smaller supported set), then select one based on latency and memory.
The backfill report must contain `batch_size`, `number_of_batches`,
`total_time` and `fragments_per_second`.

Every vector row and query must carry a conceptual `embedding_space_id` before
production backfill. Because both profiles use the same Giga Q8_0 artifact,
the preferred strategy is one portable space, `giga-0826-q8_0-v1`; hardware
backend alone must not create a new identity. It must include at least:

- source model and source revision;
- runtime artifact/precision;
- dimension;
- query-instruction version.

Compare the same test set from A100 CUDA llama.cpp and portable CPU/GPU
llama.cpp, measuring dimension, norm, cosine agreement and retrieval ordering.
Use `giga-0826-q8_0-v1` only if backend differences stay within a documented
tolerance. If they do not, stop and split the spaces. Any future BF16
experiment must use a separate ID such as `giga-0826-bf16-v1`; it is not the
default A100 plan. This is a **HIGH certification requirement** and is
intentionally not a database-schema change in this preparation task.

## Phase 5 — retrieval evaluation

Run the unchanged 44-query gold set against the same corpus and report each
configuration separately:

- **B1:** exact + PostgreSQL FTS + `pg_trgm` baseline;
- **B2:** B1 + Giga Q8_0 dense + RRF;
- **B3:** B2 + Querit, only if Querit runtime is certified in Phase 6.

Measure Recall@5, Recall@10, MRR, typo subset and exact-code subset. Record
p50, p95 and mean latency. `Recall@10 >= 0.90` is a target, not a guaranteed
result. Report misses exactly; never blend hand-crafted adversarial cases into
the unchanged gold metric.

When Querit is enabled, send the whole shortlist in one provider call where
supported (up to approximately 20 candidates). Measure `rerank-20` latency;
do not replace it with twenty serial pair calls merely for convenience.

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

Keep the project generator context capped at **8192 tokens**, even if the model
advertises a larger maximum. Verify that system instructions, selected evidence
and the structured response fit inside this envelope.

Test prompt/prefix cache reuse if the pinned llama.cpp build supports it. A
cache may contain the system prompt, structured-output instructions and
grounding rules, but never user-specific answers and never correctness-critical
state. Record whether reuse is reliable; it is a performance optimization only.

For `cloud-a100`, start generation concurrency at `parallel = 4` only as a
benchmark candidate. Compare 1, 2 and 4 slots (optionally 8 if inexpensive),
then select from p50, p95, TTFT, throughput and VRAM rather than throughput
alone. Do not make 4 a production truth.

Use high-quality KV cache (for example f16 where supported) as the A100
candidate. On portable hardware, validate the degradation order: reduce
parallelism, reduce batch sizes, use a lower-memory KV mode if validated,
partial offload, then CPU fallback. Verify exact KV types/flags against the
pinned build; do not claim support in advance.

Flash Attention is an A100 certification candidate only. Benchmark ON/OFF when
cheap and record the result; it is not pre-certified.

## Phase 8 — full pipeline

Exercise the unchanged pipeline:

`retrieval → answerability → draft → verify`

Cover answerable, condition-dependent, insufficient-evidence, exact
code/status, typo, human-required, generator-unavailable,
reranker-unavailable and embedding-unavailable cases. Confirm that no model
profile changes `.NET` decisions, state transitions, moderation, routing,
handoff, feedback or source provenance.

## Phase 9 — resource benchmark

For each available profile record real model load time, warm-up time, cold
readiness time, idle VRAM/RAM, loaded-model VRAM/RAM, peak VRAM/RAM, embedding
latency, `rerank-20` latency, TTFT where applicable, generation tokens/second,
warm-request latency and end-to-end latency at 1, 2 and 4 concurrent requests.
Optionally test 8 generation slots if cheap. Compare profiles only with their
hardware clearly labelled. Do not claim all three models fit simultaneously on
portable hardware without measurement.

On portable hardware, a short bounded startup calibration may run for at most
10–20 seconds after model load. Inspect RAM, VRAM (when available), CPU
threads and backend availability, then choose GPU offload, parallelism, batch
sizes, KV mode and threads once for the process. Do not auto-tune during request
handling or migrate placement CPU↔GPU per request.

Do not intentionally fill 100% of VRAM and do not hard-code an 80% rule. If
measured headroom approaches OOM, reduce concurrency or batching before
changing model artifacts. Record the actual headroom and the selected settings.

Run a backend-portability comparison for the same Giga Q8_0 artifact on A100
CUDA llama.cpp versus portable CPU/GPU llama.cpp, and attach the results to the
`embedding_space_id` decision.

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
