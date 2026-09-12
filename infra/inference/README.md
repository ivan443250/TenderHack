# Controlled inference runtime

Model artifacts are provisioned outside Git and mounted read-only where
practical. Inference stays on team-controlled infrastructure; the normal
runtime uses no external LLM or search API.

`llama.cpp` / `llama-server` is the primary serving runtime family for verified
GGUF artifacts in both capability profiles. The goal is one reproducible,
portable runtime family, not a claim of maximum theoretical A100 throughput.
vLLM is outside the critical path and is not required.

## Model Stack v2

- Embeddings: `ai-sage/Giga-Embeddings-instruct-3B-0826`, served from the
  `ai-babai/giga-embeddings-0826-3b-gguf` Q8_0 GGUF artifact, 2048 dimensions,
  query instruction `giga_portal_support_v1`.
- Reranking: `Querit/Querit-4B` through its custom adapter. A portable
  quantized artifact is still `TBD_UNVERIFIED`; disable it until certified.
- Generation: `empero-ai/Qwen3.8-4B-Distill` with
  `Qwen3.8-4B-Q6_K.gguf` on recent llama.cpp.

Knowledge talks to logical embedding, reranker and generator providers through
internal endpoints backed by persistent llama-server processes where enabled.
It must not assume one GPU process or one physical host. Runtime selection,
artifact hashes and performance are certification results, not implied by
downloading a file. Reasoning spans such as `<think>` are stripped and are
never exposed to users, logs or analytics.

## Capability profiles (target)

**cloud-a100** — primary-demo/high-performance target on a Linux GPU host. Use
the same verified Giga Q8_0 and Qwen3.8 Q6_K GGUF artifacts as portable and
target full GPU offload. Querit joins only after its runtime path is certified.
Enabled and certified models stay resident after warm-up; no per-request load
or normal LRU unloading is assumed. This is an accelerator profile, not a
product requirement, and is **TARGET / TO BE CERTIFIED**; no latency, memory or
compatibility result is claimed here.

**portable** — constrained-hardware profile using Giga Q8_0 GGUF and Qwen3.8
Q6_K GGUF, with startup-selected full, partial or CPU offload. If Querit has no
verified portable artifact, reranking is explicitly disabled. Reduced capability
falls back to exact + PostgreSQL FTS + `pg_trgm` + Giga dense + RRF; minimal
capability keeps exact/FTS retrieval and safe clarify, extractive or handoff
behavior when model-dependent functions are unavailable. This is **TARGET / TO
BE CERTIFIED**.

The browser never connects to inference. Endpoints are reachable only from
Knowledge, and non-local deployments require authentication. We do not log raw
user requests by default. Health/readiness probes are required, and a demo
should use a warm instance rather than intentional scale-to-zero. Team-owned
model mounts or object storage are preferred; exact Cloud.ru instance values
remain to be selected during certification.

Startup chooses backend/offload, batching, parallelism, KV mode and threads;
placement is not migrated between CPU and GPU per request. Readiness requires a
bounded warm-up smoke of every enabled provider, not merely an open HTTP
socket. The project generator context remains capped at 8192 tokens.

See `docs/plans/active/model-stack-v2-real-certification.md` for the
environmental and measurement runbook.
