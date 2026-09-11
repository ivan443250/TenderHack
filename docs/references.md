# References and source hierarchy

This file records primary sources used to design the repository instructions and technical baseline. It is not a substitute for the source itself.

## 1. Team canonical input

### TenderHack НН 2026 — финальная спецификация реализации

User-provided reviewed implementation specification, dated 11 September 2026. It is the direct basis for the product/technical decisions distilled into `docs/product-spec.md`, `docs/architecture.md`, `docs/stack.md`, `docs/quality.md` and `docs/execution-plan.md`.

Important principle from the spec: previously proposed concepts may be overturned when they conflict with formal requirements or real data. The repository docs therefore capture **current decisions**, not immutable dogma.

## 2. OpenAI guidance for coding-agent workflow

### OpenAI — Harness engineering: leveraging Codex in an agent-first world

https://openai.com/index/harness-engineering/

Used for:

- treating repository knowledge as first-class infrastructure;
- keeping `AGENTS.md` concise and navigational instead of one giant manual;
- storing deeper context in structured versioned docs;
- plans as first-class artifacts;
- agents performing implementation, review and testing;
- self-review and iterative agent-to-agent review;
- mechanically enforceable architecture where useful.

### OpenAI — A practical guide to building agents

https://openai.com/business/guides-and-resources/a-practical-guide-to-building-ai-agents/

Used for:

- manager/orchestrator pattern;
- starting with a single controlled agent and adding specialization only where complexity justifies it;
- explicit tools/instructions/guardrails;
- human intervention and risk boundaries.

The repository applies this primarily to the **product runtime**: one explicit orchestrator rather than a decorative multi-agent swarm.

### OpenAI — Model behavior / coding guidance for delegation and verification

https://model-spec.openai.com/

and the current OpenAI coding-agent/model guidance available through OpenAI documentation.

Used for:

- delegating independent subtasks when it improves speed or quality;
- keeping root-agent responsibility for integration;
- proportional verification rather than blindly running every test after every small edit;
- precise subagent task contracts.

### OpenAI — Introducing Codex

https://openai.com/index/introducing-codex/

Used for:

- repository-level `AGENTS.md` instructions;
- telling coding agents how to navigate, test and follow local conventions;
- keeping repo instructions close to code and versioned.

## 3. Model / inference sources

### Qwen3-4B-Instruct-2507

https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507

Current starting generator candidate. Final selection is subject to real hardware/quality benchmark.

### Qwen3-Embedding-0.6B

https://huggingface.co/Qwen/Qwen3-Embedding-0.6B

Current starting embedding candidate; supports up to 1024 dimensions. Benchmark on real corpus before claiming superiority.

### BAAI/bge-reranker-v2-m3

https://huggingface.co/BAAI/bge-reranker-v2-m3

Pairwise multilingual reranker candidate. Its raw/sigmoid score is relevance evidence, not automatically a calibrated probability of answer correctness.

### vLLM — Structured Outputs

https://docs.vllm.ai/en/latest/features/structured_outputs/

Used for local serving/structured schema output if hardware/runtime compatibility passes smoke testing. Structured format constrains shape, not truth.

### llama.cpp server

https://github.com/ggml-org/llama.cpp/tree/master/tools/server

Single fallback inference runtime if vLLM/model cannot run reliably on provided hardware. Not maintained as a second default serving stack.

## 4. Retrieval / persistence sources

### PostgreSQL 16 — Full Text Search controls

https://www.postgresql.org/docs/16/textsearch-controls.html

Used for lexeme/query construction and PostgreSQL ranking functions. Repository docs intentionally do not call this BM25.

### PostgreSQL `pg_trgm`

https://www.postgresql.org/docs/current/pgtrgm.html

Used for controlled typo/term similarity support, not as semantic search for entire conversations.

### pgvector

https://github.com/pgvector/pgvector

Used for initial exact vector retrieval and later optional ANN only when measured. Exact search is preferred at current corpus scale for simplicity and predictable recall.

## 5. Parsing sources

### pdfplumber

https://github.com/jsvine/pdfplumber

Lightweight baseline for text extraction and table inspection.

### Docling — chunking / document processing

https://docling-project.github.io/docling/concepts/chunking/

Optional structured parsing/chunking enhancement for pages where it measurably improves extraction. Not assumed to solve every PDF automatically.

### PyMuPDF licensing/about

https://pymupdf.readthedocs.io/en/latest/about.html

If introduced, licensing must be considered explicitly rather than added casually.

## 6. Runtime platform

### Node.js releases

https://nodejs.org/en/about/previous-releases

The baseline frontend runtime in September 2026 is Node.js 24 LTS. Pin exact image/runtime in implementation manifests.

### Python

Target baseline: Python 3.12. Pin the exact patch/minor constraints in `pyproject.toml`/container when scaffold is created.

## 7. Product/hackathon sources

Formal organizer requirements, oral clarifications and supplied data have higher priority than generic technology guidance. They are not duplicated in this repository if distribution restrictions apply.

Repository rule:

> If a mentor/organizer clarification changes a requirement, record the clarification in a non-sensitive repository note/decision where legally allowed, update the relevant source-of-truth docs and add a regression/acceptance check where possible.

## 8. Citation discipline inside the project

For engineering claims:

- prefer primary docs/model cards;
- distinguish documented capability from our benchmarked result;
- do not turn vendor claims into measured product facts;
- record model/runtime/library version/revision used in actual experiments;
- presentation numbers must come from reproducible eval output, not this planning document.
