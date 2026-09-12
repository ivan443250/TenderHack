-- Development-only role bootstrap. Runtime migrations own their respective tables.
-- Compose passes these values from API_DB_PASSWORD / KNOWLEDGE_DB_PASSWORD;
-- no credential is embedded in the SQL itself.
\set ON_ERROR_STOP on
\getenv api_db_password API_DB_PASSWORD
\getenv knowledge_db_password KNOWLEDGE_DB_PASSWORD
\if :{?api_db_password}
\else
  \echo 'API_DB_PASSWORD must be provided to the postgres init container'
  \quit 3
\endif
\if :{?knowledge_db_password}
\else
  \echo 'KNOWLEDGE_DB_PASSWORD must be provided to the postgres init container'
  \quit 3
\endif

SELECT format('CREATE ROLE api_rw LOGIN PASSWORD %L', :'api_db_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'api_rw')\gexec
SELECT format('CREATE ROLE knowledge_rw LOGIN PASSWORD %L', :'knowledge_db_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'knowledge_rw')\gexec
-- Keep re-runs safe when a pre-existing role was created without LOGIN.
ALTER ROLE api_rw LOGIN PASSWORD :'api_db_password';
ALTER ROLE knowledge_rw LOGIN PASSWORD :'knowledge_db_password';

-- Separate runtime-owned schemas keep unqualified migration/table names scoped to
-- the connecting role and make accidental cross-runtime reads fail by default.
CREATE SCHEMA IF NOT EXISTS api AUTHORIZATION api_rw;
CREATE SCHEMA IF NOT EXISTS knowledge AUTHORIZATION knowledge_rw;
ALTER ROLE api_rw SET search_path = api, public;
ALTER ROLE knowledge_rw SET search_path = knowledge, public;

-- Extensions are installed by the postgres admin during first database bootstrap,
-- before runtime roles run their own Alembic migrations.
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

DO $$
BEGIN
  EXECUTE format('GRANT CONNECT ON DATABASE %I TO api_rw, knowledge_rw', current_database());
END
$$;

-- Runtime roles must use explicitly owned tables; the default public schema is not
-- a shared write surface. Migrations grant only the DML needed by each owner.
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO api_rw, knowledge_rw;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
