"""Create the knowledge-owned durable quality analytics foundation.

Quality payloads are copies pushed by api-worker.  This migration deliberately
does not reference any API-owned table and can therefore be applied to a
database where support-core tables are absent.
"""

from alembic import op
import sqlalchemy as sa


revision = "0008_quality_analytics"
down_revision = "0007_giga_2048_embeddings"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "quality_cases",
        sa.Column("delivery_key", sa.String(256), primary_key=True),
        sa.Column("case_id", sa.String(256), nullable=False),
        sa.Column("turn_id", sa.String(256), nullable=False),
        sa.Column("revision", sa.Integer(), nullable=False),
        sa.Column("source_type", sa.String(64), nullable=False),
        sa.Column("received_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("occurred_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("payload_hash", sa.String(64), nullable=False),
        sa.Column("provenance", sa.JSON(), nullable=False),
        sa.Column("question_text", sa.Text(), nullable=False),
        sa.Column("prior_turns_summary", sa.Text()),
        sa.Column("decision_label", sa.String(128), nullable=False),
        sa.Column("reason_codes", sa.JSON(), nullable=False),
        sa.Column("answer_markdown", sa.Text()),
        sa.Column("evidence_fragment_ids", sa.JSON()),
        sa.Column("snapshot_id", sa.String(256)),
        sa.Column("handoff_status_label", sa.String(128)),
        sa.Column("recommended_line", sa.Text()),
        sa.Column("service_need", sa.String(256)),
        sa.Column("stage_timings", sa.JSON(), nullable=False),
        sa.Column("error_category", sa.String(128)),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.UniqueConstraint("turn_id", "revision", name="uq_quality_cases_turn_revision"),
    )
    op.create_index("ix_quality_cases_case_occurred", "quality_cases", ["case_id", "occurred_at"])
    op.create_index("ix_quality_cases_payload_hash", "quality_cases", ["payload_hash"])

    op.create_table(
        "quality_feedback",
        sa.Column("feedback_id", sa.String(256), primary_key=True),
        sa.Column("case_id", sa.String(256), nullable=False),
        sa.Column("turn_id", sa.String(256), nullable=False),
        sa.Column("source_type", sa.String(64), nullable=False),
        sa.Column("received_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("occurred_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("payload_hash", sa.String(64), nullable=False),
        sa.Column("provenance", sa.JSON(), nullable=False),
        sa.Column("specialist_rating", sa.String(16)),
        sa.Column("information_quality_rating", sa.String(16)),
        sa.Column("helpful", sa.Boolean()),
        sa.Column("specialist_ref", sa.String(256)),
        sa.Column("integration_mode", sa.String(32)),
        sa.Column("solved", sa.Boolean()),
        sa.Column("comment_text", sa.Text()),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
    )
    op.create_index("ix_quality_feedback_case_occurred", "quality_feedback", ["case_id", "occurred_at"])

    op.create_table(
        "quality_completions",
        sa.Column("case_id", sa.String(256), primary_key=True),
        sa.Column("completed_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("source_type", sa.String(64), nullable=False),
        sa.Column("received_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("payload_hash", sa.String(64), nullable=False),
        sa.Column("provenance", sa.JSON(), nullable=False),
        sa.Column("completion_reason", sa.String(256), nullable=False),
        sa.Column("resolution_status", sa.String(128), nullable=False),
        sa.Column("handoff_status", sa.String(128)),
        sa.Column("integration_mode", sa.String(32)),
        sa.Column("specialist_ref", sa.String(256)),
        sa.Column("stage_code", sa.String(128)),
        sa.Column("moderation_warning_count", sa.Integer()),
        sa.Column("turn_count", sa.Integer()),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
    )
    op.create_index("ix_quality_completions_status", "quality_completions", ["resolution_status", "completed_at"])

    op.create_table(
        "quality_evaluations",
        sa.Column("evaluation_id", sa.String(512), primary_key=True),
        sa.Column("case_id", sa.String(256), nullable=False),
        sa.Column("turn_id", sa.String(256), nullable=False),
        sa.Column("revision", sa.Integer(), nullable=False),
        sa.Column("factual_support", sa.JSON(), nullable=False),
        sa.Column("completeness", sa.JSON(), nullable=False),
        sa.Column("clarity", sa.JSON(), nullable=False),
        sa.Column("next_step", sa.JSON(), nullable=False),
        sa.Column("critical_error", sa.Boolean(), nullable=False),
        sa.Column("critical_error_reason", sa.Text()),
        sa.Column("provenance", sa.JSON(), nullable=False),
        sa.Column("evaluated_at", sa.DateTime(timezone=True), nullable=False),
        sa.UniqueConstraint("turn_id", "revision", name="uq_quality_evaluations_turn_revision"),
    )
    op.create_index("ix_quality_evaluations_case_evaluated", "quality_evaluations", ["case_id", "evaluated_at"])

    op.create_table(
        "issue_groups",
        sa.Column("group_key", sa.String(512), primary_key=True),
        sa.Column("label", sa.Text(), nullable=False),
        sa.Column("definition", sa.Text(), nullable=False),
        sa.Column("sample_size", sa.Integer(), nullable=False),
        sa.Column("representative_case_ids", sa.JSON(), nullable=False),
        sa.Column("negative_signal_count", sa.Integer(), nullable=False),
        sa.Column("unresolved_count", sa.Integer(), nullable=False),
        sa.Column("limitations", sa.JSON(), nullable=False),
        sa.Column("hypotheses", sa.JSON(), nullable=False),
        sa.Column("provenance", sa.JSON(), nullable=False),
        sa.Column("generated_at", sa.DateTime(timezone=True), nullable=False),
    )
    op.create_index("ix_issue_groups_sample_size", "issue_groups", ["sample_size"])

    op.create_table(
        "issue_group_members",
        sa.Column("group_key", sa.String(512), nullable=False),
        sa.Column("delivery_key", sa.String(256), nullable=False),
        sa.Column("case_id", sa.String(256), nullable=False),
        sa.PrimaryKeyConstraint("group_key", "delivery_key"),
    )
    op.create_index("ix_issue_group_members_case", "issue_group_members", ["case_id"])

    # The role is created by the PostgreSQL bootstrap, just like previous
    # knowledge migrations.  Grants are limited to these knowledge-owned
    # objects; there are no API-table privileges here.
    op.execute(
        "GRANT SELECT, INSERT, UPDATE, DELETE ON "
        "quality_cases, quality_feedback, quality_completions, quality_evaluations, "
        "issue_groups, issue_group_members TO knowledge_rw"
    )


def downgrade() -> None:
    op.drop_index("ix_issue_group_members_case", table_name="issue_group_members")
    op.drop_table("issue_group_members")
    op.drop_index("ix_issue_groups_sample_size", table_name="issue_groups")
    op.drop_table("issue_groups")
    op.drop_index("ix_quality_evaluations_case_evaluated", table_name="quality_evaluations")
    op.drop_table("quality_evaluations")
    op.drop_index("ix_quality_completions_status", table_name="quality_completions")
    op.drop_table("quality_completions")
    op.drop_index("ix_quality_feedback_case_occurred", table_name="quality_feedback")
    op.drop_table("quality_feedback")
    op.drop_index("ix_quality_cases_payload_hash", table_name="quality_cases")
    op.drop_index("ix_quality_cases_case_occurred", table_name="quality_cases")
    op.drop_table("quality_cases")
