# References and source hierarchy

This file records primary sources for the current repository decisions. Product
rules and public contracts remain unchanged by the Model Stack V2 migration.

## Team canonical input

The reviewed TenderHack implementation specification and organizer materials
remain the source for product behaviour. This repository documents the current
runtime decision; the external historical implementation spec is not rewritten.

## Model / inference sources

### Giga Embeddings 3B 0826 (source model)

https://huggingface.co/ai-sage/Giga-Embeddings-instruct-3B-0826

Russian/English mean-pooled, L2-normalized 2048-dimensional embeddings. Query
retrieval uses the versioned `giga_portal_support_v1` instruction; documents
remain plain text.

### Giga Embeddings GGUF (independent runtime artifact)

https://huggingface.co/ai-babai/giga-embeddings-0826-3b-gguf

The Q8_0 file is an **INDEPENDENT GGUF CONVERSION**, not an official ai-sage
release. Its exact hash is recorded in `src/knowledge/model-manifest.json`.

### Querit/Querit-4B

https://huggingface.co/Querit/Querit-4B

Custom `AutoModel` multilingual reranker. The adapter consumes the upstream
`score` output as raw relevance evidence, not a calibrated probability. A
trusted Q6-class runtime artifact is not yet verified.

### Qwen3.8-4B-Distill

https://huggingface.co/empero-ai/Qwen3.8-4B-Distill

### Qwen3.8-4B-Distill-GGUF

https://huggingface.co/empero-ai/Qwen3.8-4B-Distill-GGUF

The Q6_K GGUF is the local generation artifact; recent llama.cpp is required
for the Qwen3.5/Gated DeltaNet architecture. `<think>` output is private and
removed before structured parsing.

### llama.cpp server

https://github.com/ggml-org/llama.cpp/tree/master/tools/server

Active local runtime for embedding and generation through internal
OpenAI-compatible endpoints. No external AI API is used.

## Retrieval / persistence sources

### PostgreSQL 16 Full Text Search

https://www.postgresql.org/docs/16/textsearch-controls.html

### PostgreSQL `pg_trgm`

https://www.postgresql.org/docs/current/pgtrgm.html

### pgvector

https://github.com/pgvector/pgvector

Exact vector scan remains the default at the current corpus scale; no HNSW or
IVFFlat index is assumed.

## Parsing sources

### pdfplumber

https://github.com/jsvine/pdfplumber

### Docling

https://docling-project.github.io/docling/concepts/chunking/

Docling/OCR remain targeted options for measured extraction failures only.
