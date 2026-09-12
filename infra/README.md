# Infrastructure scaffold

`compose.yaml` is the single-host target topology. PostgreSQL is the only shared infrastructure system; API and Knowledge use separate runtime roles, schemas and migrations. The first-database bootstrap installs `pgvector`/`pg_trgm` as the admin and reads role passwords from `API_DB_PASSWORD` and `KNOWLEDGE_DB_PASSWORD` (the Compose defaults are development-only). Inference is profile-gated until a local model smoke test succeeds.

The role/extension script runs only when PostgreSQL initializes a fresh data volume; apply equivalent reviewed SQL through the normal database change process when bootstrapping an existing volume.
