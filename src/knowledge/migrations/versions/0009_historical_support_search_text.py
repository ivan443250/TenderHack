"""Separate historical ticket search text from cited support solutions."""

from alembic import op
import sqlalchemy as sa


revision = "0009_historical_support_search_text"
down_revision = "0008_quality_analytics"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column("kb_fragments", sa.Column("search_text", sa.Text(), nullable=True))
    op.execute(
        "CREATE INDEX ix_kb_fragments_search_text_fts_russian "
        "ON kb_fragments USING gin (to_tsvector('russian', search_text)) "
        "WHERE search_text IS NOT NULL"
    )
    op.execute(
        "CREATE INDEX ix_kb_fragments_search_text_trgm "
        "ON kb_fragments USING gin (search_text gin_trgm_ops) "
        "WHERE search_text IS NOT NULL"
    )


def downgrade() -> None:
    op.execute("DROP INDEX IF EXISTS ix_kb_fragments_search_text_trgm")
    op.execute("DROP INDEX IF EXISTS ix_kb_fragments_search_text_fts_russian")
    op.drop_column("kb_fragments", "search_text")
