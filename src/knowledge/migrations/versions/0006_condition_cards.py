"""Versioned, knowledge-owned condition cards."""

from alembic import op
import sqlalchemy as sa


revision = "0006_condition_cards"
down_revision = "0005_dense_fragment_embeddings"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "kb_condition_cards",
        sa.Column("card_id", sa.String(128), nullable=False),
        sa.Column("version", sa.String(64), nullable=False),
        sa.Column("snapshot_id", sa.String(128), nullable=False),
        sa.Column("title", sa.Text(), nullable=False),
        sa.Column("applicability", sa.JSON(), nullable=False),
        sa.Column("required_slots", sa.JSON(), nullable=False),
        sa.Column("allowed_action", sa.Text(), nullable=False),
        sa.Column("forbidden_generalization", sa.Text(), nullable=False),
        sa.Column("handoff_condition", sa.Text(), nullable=False),
        sa.Column("risk_level", sa.String(8), nullable=False),
        sa.Column("source_fragment_ids", sa.JSON(), nullable=False),
        sa.Column("source_quotes", sa.JSON(), nullable=False),
        sa.Column("source_anchors", sa.JSON(), nullable=False),
        sa.Column("review_status", sa.String(32), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()),
        sa.PrimaryKeyConstraint("card_id", "version"),
        sa.CheckConstraint("risk_level IN ('LOW', 'MEDIUM', 'HIGH')", name="ck_kb_condition_cards_risk"),
    )
    op.create_index("ix_kb_condition_cards_snapshot_status", "kb_condition_cards", ["snapshot_id", "review_status"])
    op.execute("GRANT SELECT, INSERT, UPDATE, DELETE ON kb_condition_cards TO knowledge_rw")


def downgrade() -> None:
    op.drop_index("ix_kb_condition_cards_snapshot_status", table_name="kb_condition_cards")
    op.drop_table("kb_condition_cards")
