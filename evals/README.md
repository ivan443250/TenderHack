# Evaluation workspace

This directory is reserved for repository-level gold cases, decision/retrieval/moderation/quality regressions and cross-component evaluation artifacts that are safe to commit. Runtime-specific benchmark evidence currently also lives under `src/knowledge/benchmarks/`.

Do not commit organizer raw data, private user records or model weights. Every published metric must identify the evaluated snapshot/configuration and `n`; historical benchmark artifacts must not be relabelled as results for a newer model/runtime stack.

The authoritative evaluation policy is `../docs/quality.md`.
