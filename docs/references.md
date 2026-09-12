# References and source hierarchy

This file records primary external sources used by the current repository decisions. Product rules and public contracts are defined inside this repository; an external guide is evidence/rationale, not authority over explicit organizer/team requirements.

## Team canonical input

The reviewed TenderHack implementation specification and organizer materials remain the source for product behaviour. This repository documents the current runtime decision; the external historical implementation spec is not rewritten.

## Agent-development guidance

### OpenAI — Harness engineering: leveraging Codex in an agent-first world

https://openai.com/index/harness-engineering/

Used for the repository-knowledge pattern: keep `AGENTS.md` as a compact map and keep detailed, versioned knowledge in structured repository docs; separate active/completed execution plans and keep documentation inspectable rather than one monolithic instruction file.

### OpenAI — How OpenAI uses Codex

https://openai.com/business/guides-and-resources/how-openai-uses-codex/

Used for practical coding-agent guidance: persistent repository context through `AGENTS.md`, scoped issue-like tasks, planning before large changes and iterative review/verification. Our manager/subagent/skeptic protocol is a repository policy built on these principles; it is not claimed to be a verbatim OpenAI workflow.

### OpenAI — Unrolling the Codex agent loop

https://openai.com/index/unrolling-the-codex-agent-loop/

Used to verify how repository/user instruction layers such as `AGENTS.md` enter the Codex context. It does not change this repository's product architecture.

## Model / inference sources

### Giga Embeddings 3B 0826 (source model)

https://huggingface.co/ai-sage/Giga-Embeddings-instruct-3B-0826

Russian/English mean-pooled, L2-normalized 2048-dimensional embeddings. Query retrieval uses the versioned `giga_portal_support_v1` instruction; documents remain plain text.

### Giga Embeddings GGUF (independent runtime artifact)

https://huggingface.co/ai-babai/giga-embeddings-0826-3b-gguf

The Q8_0 file is an **INDEPENDENT GGUF CONVERSION**, not an official ai-sage release. Its exact hash is recorded in `src/knowledge/model-manifest.json`.

### Querit/Querit-4B

https://huggingface.co/Querit/Querit-4B

Custom `AutoModel` multilingual reranker. The adapter consumes the upstream `score` output as raw relevance evidence, not a calibrated probability. A trusted quantized runtime artifact is not yet certified.

### Qwen3.8-4B-Distill

https://huggingface.co/empero-ai/Qwen3.8-4B-Distill

### Qwen3.8-4B-Distill-GGUF

https://huggingface.co/empero-ai/Qwen3.8-4B-Distill-GGUF

The Q6_K GGUF is the selected local generation artifact for certification; recent llama.cpp is required for the architecture. `<think>` output is private and removed before structured parsing. Selection is accepted by ADR-0003 but runtime quality/performance still requires the certification plan; do not turn artifact selection into a benchmark claim.

### llama.cpp server

https://github.com/ggml-org/llama.cpp/tree/master/tools/server

Selected local runtime family for embedding and generation through internal OpenAI-compatible endpoints. No external AI API is used in the normal intelligent runtime.

## Retrieval / persistence sources

### PostgreSQL 16 Full Text Search

https://www.postgresql.org/docs/16/textsearch-controls.html

### PostgreSQL `pg_trgm`

https://www.postgresql.org/docs/current/pgtrgm.html

### pgvector

https://github.com/pgvector/pgvector

Exact vector scan remains the default at the current corpus scale; no HNSW or IVFFlat index is assumed.

## Parsing sources

### pdfplumber

https://github.com/jsvine/pdfplumber

### Docling

https://docling-project.github.io/docling/concepts/chunking/

Docling/OCR remain targeted options for measured extraction failures only.
