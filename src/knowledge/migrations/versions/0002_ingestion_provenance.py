"""Knowledge-owned document, fragment and immutable snapshot foundation.

Fragments are canonical, version-owned records.  Snapshot membership is a
separate association so republishing an unchanged version cannot rewrite an
older snapshot's provenance.
"""

from alembic import op
import sqlalchemy as sa


revision = "0002_ingestion_provenance"
down_revision = "0001_knowledge_baseline"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "kb_documents",
        sa.Column("document_id", sa.String(128), primary_key=True),
        sa.Column("original_filename", sa.Text(), nullable=False),
        sa.Column("content_sha256", sa.String(64), nullable=False),
        sa.Column("corpus", sa.String(16), nullable=False),
        sa.Column("declared_version", sa.String(128), nullable=True),
        sa.Column("declared_date", sa.Date(), nullable=True),
        sa.Column("page_count", sa.Integer(), nullable=False),
        sa.Column("source_reference", sa.Text(), nullable=False),
        sa.Column("ingested_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("review_status", sa.String(32), nullable=False),
        sa.CheckConstraint("corpus IN ('NORMATIVE', 'HISTORICAL')", name="ck_kb_documents_corpus"),
    )
    op.create_table(
        "kb_document_versions",
        sa.Column("document_version_id", sa.String(128), primary_key=True),
        sa.Column("document_id", sa.String(128), nullable=False),
        sa.Column("content_sha256", sa.String(64), nullable=False),
        sa.Column("declared_version", sa.String(128), nullable=True),
        sa.Column("declared_date", sa.Date(), nullable=True),
        sa.Column("page_count", sa.Integer(), nullable=False),
        sa.Column("source_reference", sa.Text(), nullable=False),
        sa.Column("ingested_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("review_status", sa.String(32), nullable=False),
        sa.ForeignKeyConstraint(["document_id"], ["kb_documents.document_id"]),
        sa.UniqueConstraint(
            "document_id",
            "content_sha256",
            "declared_version",
            "declared_date",
            name="uq_kb_document_versions_content",
        ),
    )
    op.create_table(
        "kb_snapshots",
        sa.Column("snapshot_id", sa.String(128), primary_key=True),
        sa.Column("corpus", sa.String(16), nullable=False),
        sa.Column("manifest_hash", sa.String(64), nullable=False, unique=True),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("review_status", sa.String(32), nullable=False),
        sa.CheckConstraint("corpus IN ('NORMATIVE', 'HISTORICAL')", name="ck_kb_snapshots_corpus"),
    )
    op.create_table(
        "kb_snapshot_document_versions",
        sa.Column("snapshot_id", sa.String(128), nullable=False),
        sa.Column("document_version_id", sa.String(128), nullable=False),
        sa.PrimaryKeyConstraint("snapshot_id", "document_version_id"),
        sa.ForeignKeyConstraint(["snapshot_id"], ["kb_snapshots.snapshot_id"]),
        sa.ForeignKeyConstraint(["document_version_id"], ["kb_document_versions.document_version_id"]),
    )
    op.create_table(
        "kb_fragments",
        sa.Column("fragment_id", sa.String(128), primary_key=True),
        sa.Column("document_version_id", sa.String(128), nullable=False),
        sa.Column("page_start", sa.Integer(), nullable=False),
        sa.Column("page_end", sa.Integer(), nullable=False),
        sa.Column("section", sa.Text(), nullable=True),
        sa.Column("kind", sa.String(32), nullable=False),
        sa.Column("heading_path", sa.JSON(), nullable=False),
        sa.Column("text", sa.Text(), nullable=False),
        sa.Column("source_anchor", sa.JSON(), nullable=False),
        sa.Column("actor_roles", sa.JSON(), nullable=True),
        sa.Column("process", sa.Text(), nullable=True),
        sa.Column("provider", sa.Text(), nullable=True),
        sa.Column("document_status", sa.String(64), nullable=True),
        sa.Column("error_codes", sa.JSON(), nullable=True),
        sa.Column("review_status", sa.String(32), nullable=False),
        sa.Column("text_hash", sa.String(64), nullable=False),
        sa.ForeignKeyConstraint(["document_version_id"], ["kb_document_versions.document_version_id"]),
    )
    op.create_table(
        "kb_snapshot_fragments",
        sa.Column("snapshot_id", sa.String(128), nullable=False),
        sa.Column("fragment_id", sa.String(128), nullable=False),
        sa.PrimaryKeyConstraint("snapshot_id", "fragment_id"),
        sa.ForeignKeyConstraint(["snapshot_id"], ["kb_snapshots.snapshot_id"]),
        sa.ForeignKeyConstraint(["fragment_id"], ["kb_fragments.fragment_id"]),
    )
    op.add_column("kb_ingestion_runs", sa.Column("corpus", sa.String(16), nullable=True))
    op.add_column("kb_ingestion_runs", sa.Column("document_version_ids", sa.JSON(), nullable=True))
    op.add_column("kb_ingestion_runs", sa.Column("completed_at", sa.DateTime(timezone=True), nullable=True))
    op.add_column("kb_ingestion_runs", sa.Column("warnings", sa.JSON(), nullable=True))
    op.add_column("kb_ingestion_runs", sa.Column("error", sa.Text(), nullable=True))
    op.create_index("ix_kb_document_versions_document_id", "kb_document_versions", ["document_id"])
    op.create_index("ix_kb_snapshot_fragments_fragment", "kb_snapshot_fragments", ["fragment_id"])
    op.create_index("ix_kb_fragments_document_version", "kb_fragments", ["document_version_id"])
    op.execute("GRANT SELECT, INSERT, UPDATE, DELETE ON kb_documents, kb_document_versions, kb_snapshots, kb_snapshot_document_versions, kb_fragments, kb_snapshot_fragments, kb_ingestion_runs TO knowledge_rw")


def downgrade() -> None:
    op.drop_index("ix_kb_fragments_document_version", table_name="kb_fragments")
    op.drop_index("ix_kb_snapshot_fragments_fragment", table_name="kb_snapshot_fragments")
    op.drop_index("ix_kb_document_versions_document_id", table_name="kb_document_versions")
    op.drop_column("kb_ingestion_runs", "error")
    op.drop_column("kb_ingestion_runs", "warnings")
    op.drop_column("kb_ingestion_runs", "completed_at")
    op.drop_column("kb_ingestion_runs", "document_version_ids")
    op.drop_column("kb_ingestion_runs", "corpus")
    op.drop_table("kb_snapshot_fragments")
    op.drop_table("kb_fragments")
    op.drop_table("kb_snapshot_document_versions")
    op.drop_table("kb_snapshots")
    op.drop_table("kb_document_versions")
    op.drop_table("kb_documents")
