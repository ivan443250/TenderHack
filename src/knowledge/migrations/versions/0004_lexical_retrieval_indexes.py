"""Indexes for the K2A lexical retrieval baseline."""

from alembic import op


revision = "0004_lexical_retrieval_indexes"
down_revision = "0003_document_version_semantics"
branch_labels = None
depends_on = None


def upgrade() -> None:
    # pg_trgm and the Russian text search configuration are provisioned by the
    # PostgreSQL bootstrap.  Runtime migrations only own these knowledge indexes.
    op.execute(
        "CREATE INDEX IF NOT EXISTS ix_kb_fragments_text_trgm "
        "ON kb_fragments USING gin (text gin_trgm_ops)"
    )
    op.execute(
        "CREATE INDEX IF NOT EXISTS ix_kb_fragments_fts_russian "
        "ON kb_fragments USING gin (to_tsvector('russian', text))"
    )


def downgrade() -> None:
    op.execute("DROP INDEX IF EXISTS ix_kb_fragments_fts_russian")
    op.execute("DROP INDEX IF EXISTS ix_kb_fragments_text_trgm")

