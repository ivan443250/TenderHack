"""Migrate derived dense vectors from legacy Qwen 1024 to Giga 2048.

Embeddings are disposable derived data.  The migration invalidates every old
row before changing the pgvector typmod, while leaving documents, fragments,
snapshots and condition cards untouched.
"""

from alembic import op


revision = "0007_giga_2048_embeddings"
down_revision = "0006_condition_cards"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.execute("DELETE FROM kb_fragment_embeddings")
    op.execute(
        "ALTER TABLE kb_fragment_embeddings "
        "DROP CONSTRAINT IF EXISTS ck_kb_fragment_embeddings_dimension"
    )
    op.execute(
        "ALTER TABLE kb_fragment_embeddings "
        "ALTER COLUMN embedding TYPE vector(2048) USING embedding::vector"
    )
    op.create_check_constraint(
        "ck_kb_fragment_embeddings_dimension",
        "kb_fragment_embeddings",
        "dimension = 2048",
    )


def downgrade() -> None:
    # Downgrading cannot recover invalidated legacy vectors.  The table is
    # empty by contract after the dimension migration; make that explicit.
    op.execute("DELETE FROM kb_fragment_embeddings")
    op.execute(
        "ALTER TABLE kb_fragment_embeddings "
        "DROP CONSTRAINT IF EXISTS ck_kb_fragment_embeddings_dimension"
    )
    op.execute(
        "ALTER TABLE kb_fragment_embeddings "
        "ALTER COLUMN embedding TYPE vector(1024) USING embedding::vector"
    )
    op.create_check_constraint(
        "ck_kb_fragment_embeddings_dimension",
        "kb_fragment_embeddings",
        "dimension = 1024",
    )
