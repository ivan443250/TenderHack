"""Persist Qwen3 1024-dimensional fragment embeddings.

The table is knowledge-owned and intentionally has no approximate-nearest
neighbour index.  The normative corpus is small enough for an exact scan;
keeping the migration minimal also avoids committing to an index before a
measured workload justifies it.
"""

from alembic import op


revision = "0005_dense_fragment_embeddings"
down_revision = "0004_lexical_retrieval_indexes"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.execute(
        """
        CREATE TABLE kb_fragment_embeddings (
            fragment_id VARCHAR(128) PRIMARY KEY
                REFERENCES kb_fragments(fragment_id) ON DELETE CASCADE,
            model_id TEXT NOT NULL,
            model_revision TEXT NOT NULL,
            dimension INTEGER NOT NULL,
            embedding vector(1024) NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            CONSTRAINT ck_kb_fragment_embeddings_dimension CHECK (dimension = 1024)
        )
        """
    )
    op.create_index(
        "ix_kb_fragment_embeddings_model",
        "kb_fragment_embeddings",
        ["model_id", "model_revision", "dimension"],
        if_not_exists=True,
    )
    op.execute(
        "GRANT SELECT, INSERT, UPDATE, DELETE ON kb_fragment_embeddings TO knowledge_rw"
    )


def downgrade() -> None:
    op.drop_index("ix_kb_fragment_embeddings_model", table_name="kb_fragment_embeddings")
    op.drop_table("kb_fragment_embeddings")
