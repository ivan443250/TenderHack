# ADR-0003 — Switch local AI runtime to Giga, Querit and Qwen3.8 Distill

- **Status:** ACCEPTED WITH RUNTIME CERTIFICATION GATES (2026-09-12)
- **Scope:** Knowledge inference and derived dense-vector storage only
- **Supersedes:** the initial model/runtime hypotheses in the historical implementation specification

## Context

The first implementation used Qwen3 Embedding 0.6B (1024 dimensions),
BAAI/bge-reranker-v2-m3 and a vLLM-oriented Qwen3 generator. Those choices were
starting hypotheses, not product rules. The repository now has a pinned local
stack better suited to Russian support retrieval, while the six-document
corpus, provenance, answerability, verification and .NET ownership boundaries
remain unchanged.

## Decision

- Use `ai-sage/Giga-Embeddings-instruct-3B-0826` at the pinned source revision,
  served through the independently converted `ai-babai` Q8_0 GGUF and a local
  llama.cpp `/v1/embeddings` endpoint. Store normalized 2048-dimensional
  vectors. Apply the versioned `giga_portal_support_v1` instruction to queries
  only; documents remain plain text.
- Use `Querit/Querit-4B` at its pinned source revision through a dedicated
  custom adapter. No quantized runtime artifact is treated as certified yet;
  the default retrieval path therefore remains exact + PostgreSQL FTS + Giga
  dense + RRF without a reranker.
- Use `empero-ai/Qwen3.8-4B-Distill` with the pinned Q6_K GGUF and a recent local
  llama.cpp `/v1/chat/completions` endpoint. Generation remains evidence-gated
  and deterministically verified.
- Keep embedding, reranker and generator endpoints independently configurable;
  they may share a host but are not one assumed all-model process. Compose
  services are profile-gated and model files are mounted read-only outside git.
- Migrate the derived embedding column from `vector(1024)` to `vector(2048)`
  in a new migration. Old vectors are invalidated; documents, fragments,
  snapshots, memberships, cards and provenance are preserved.

## Reasoning privacy and ownership

The local generator client removes `<think>...</think>` spans and fails closed
on malformed or unterminated markers before parsing. Reasoning is never sent to
the browser, Support Core, persistence or quality analytics. The generator has
no authority over answerability, verification truth, routing, handoff or any
business decision; .NET remains the sole owner of those decisions and state.

## Consequences

- All dense vectors must be recomputed for the active snapshot after migration.
- Query-vector metadata and persistence guards reject legacy Qwen model/revision
  and 1024-dimensional rows.
- Runtime memory pressure is higher: Giga and Qwen3.8 are each multi-gigabyte
  artifacts, and Querit is an additional optional model. Concurrent residency
  on a 6 GB GPU is not assumed or claimed.
- Real Giga backfill, retrieval evaluation, Querit scoring smoke and generator
  structured-output/latency tests are explicit certification gates, not results
  of this migration.
- Historical K0–K6 benchmark numbers remain historical and are not relabelled
  as V2 measurements.

## Fallback and failure policy

- Exact and PostgreSQL FTS retrieval remain available without dense or Querit.
- Giga dense + RRF is the default V2 fallback when Querit is unavailable.
- If the local generator is unavailable or returns invalid structured output,
  no generated answer is emitted; deterministic answerability and verification
  remain mandatory.
- No external LLM or search API is introduced.

## Revisit when

Revisit this ADR only after the V2 certification task records real backfill
coverage, B1/B2 retrieval measurements, any Querit B3 artifact and score smoke,
Qwen3.8 structured-generation results, latency, memory and offline smoke.
