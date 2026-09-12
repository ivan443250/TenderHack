# Infrastructure

`compose.yaml` is the single-host development/demo topology. PostgreSQL is the only shared infrastructure system; API and Knowledge use separate runtime roles, schemas and migrations. The first-database bootstrap installs `pgvector`/`pg_trgm` as admin and reads role passwords from `API_DB_PASSWORD` / `KNOWLEDGE_DB_PASSWORD` (Compose defaults are development-only).

## Inference profiles

Inference artifacts live under `infra/inference/` and are intentionally profile-specific:

- `portable/` — local NVIDIA/limited-VRAM profile added for measured portable-runtime work;
- `cloudru/` / bundled assets — cloud/A100-oriented packaging and shared gateway/verification assets where present;
- Querit remains disabled unless its runtime/scoring path passes the active certification plan.

A Dockerfile/profile existing in git is **not** proof that a model/provider is certified. Capability readiness requires the smoke/benchmark evidence from `../docs/plans/active/model-stack-v2-real-certification.md`; selected model policy is ADR-0003.

Model weights/organizer data remain outside normal git history except where explicitly governed by the repository's reviewed artifact policy; runtime code must not silently download external AI models during the offline/demo path.

The role/extension bootstrap runs only when PostgreSQL initializes a fresh data volume. For an existing volume, apply equivalent reviewed SQL through the normal database change process.
