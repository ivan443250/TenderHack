# generator-stub

Test double for the OpenAI-compatible `/v1/chat/completions` generator endpoint (`docs/plans/active/2026-09-demo-readiness.md`, A1). It is extractive and verbatim: every claim it returns is a literal substring of the evidence it was given, so `verify_v1` (deterministic claim verification) passes for real instead of being bypassed.

This is a **test double for local E2E without RunPod/GPU**, not a runtime policy and not a model — see `AGENTS.md §8` ("stub/fixture mode is a testing capability, not a product fallback") and ADR-0003. It answers behind `docker compose --profile stub`; it is never started by a plain `docker compose up`, and `AI_ANSWER.model_version` always contains `stub-extractive-v0` so its output can never be mistaken for real model quality on a demo or in analytics.

Real inference is llama.cpp (`generator-inference`, `--profile generator`) or RunPod — see `docs/stack.md`. Switching between stub/real/RunPod is a `.env` change only (`KNOWLEDGE_GENERATOR_BASE_URL`/`KNOWLEDGE_GENERATOR_MODEL`); no code path branches on which one is running.
