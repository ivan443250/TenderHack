"""Keep concrete source facts on document versions, not logical documents."""

from alembic import op
import sqlalchemy as sa


revision = "0003_document_version_semantics"
down_revision = "0002_ingestion_provenance"
branch_labels = None
depends_on = None


_VERSION_FACT_COLUMNS = (
    ("content_sha256", sa.String(64)),
    ("declared_version", sa.String(128)),
    ("declared_date", sa.Date()),
    ("page_count", sa.Integer()),
    ("source_reference", sa.Text()),
    ("ingested_at", sa.DateTime(timezone=True)),
    ("review_status", sa.String(32)),
)


def upgrade() -> None:
    for column_name, _column_type in _VERSION_FACT_COLUMNS:
        op.drop_column("kb_documents", column_name)


def downgrade() -> None:
    # These fields are intentionally nullable on downgrade: their authoritative
    # values live on kb_document_versions and cannot be reconstructed for a
    # logical document without choosing an arbitrary version.
    for column_name, column_type in reversed(_VERSION_FACT_COLUMNS):
        op.add_column("kb_documents", sa.Column(column_name, column_type, nullable=True))
