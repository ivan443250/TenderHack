# ADR-0004 — Keep lexical retrieval on the production critical path

- **Status:** ACCEPTED (2026-09-13)
- **Scope:** Knowledge `/v0/retrieve` runtime selection
- **Supersedes:** the production-retrieval portion of ADR-0003; model selection and embedding storage remain unchanged

## Context

AI-2B measured the current frozen normative snapshot (3899 fragments) and the
unchanged 44-query `k2a-gold.json` set using real Giga 2048-dimensional
embeddings. The lexical baseline was exact + PostgreSQL FTS + `pg_trgm`.

Lexical Recall@10 was 0.7500. Dense-only Recall@10 was 0.5682 and Recall@30
was 0.7045. Of eleven lexical top-10 misses, dense retrieval rescued none by
rank 20 and two by rank 30. Candidate unions with dense top 3/5/10 did not
increase candidate recall. Every protected dense-fill policy reduced
Recall@10 and introduced regressions; P0 (lexical top-10) preserved exact-code
and typo behavior.

## Decision

- `/v0/retrieve` uses `LexicalRetriever` directly with `mode="hybrid"`,
  `allow_trigram=True` and the existing `lexical-v1` configuration.
- Giga adapter, 2048-dimensional embedding storage, pgvector and hybrid/RRF
  evaluation tooling remain available as experimental benchmark capability.
- Giga is not constructed, called, or used for ranking on the normal retrieve
  request path. No automatic semantic fallback or score threshold is added.
- This decision does not change the frozen HTTP contract, snapshots, corpus,
  ingestion, answerability, generation or .NET ownership boundaries.

## Revisit

Re-open only with a new measured experiment on an approved corpus/gold split
that demonstrates incremental rescue without lexical exact-code/typo
regression and with acceptable latency. Do not tune the current 44 cases into
the production policy.
