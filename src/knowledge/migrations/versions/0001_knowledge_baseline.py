"""Minimal knowledge-owned job and ingestion baseline."""

from alembic import op
import sqlalchemy as sa

revision = "0001_knowledge_baseline"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "knowledge_jobs",
        sa.Column("job_id", sa.String(128), primary_key=True),
        sa.Column("job_type", sa.String(64), nullable=False),
        sa.Column("status", sa.String(32), nullable=False),
        sa.Column("payload", sa.JSON(), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()),
    )
    op.create_table(
        "kb_ingestion_runs",
        sa.Column("run_id", sa.String(128), primary_key=True),
        sa.Column("status", sa.String(32), nullable=False),
        sa.Column("source_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()),
    )
    op.execute("GRANT SELECT, INSERT, UPDATE, DELETE ON knowledge_jobs, kb_ingestion_runs TO knowledge_rw")


def downgrade() -> None:
    op.drop_table("kb_ingestion_runs")
    op.drop_table("knowledge_jobs")
