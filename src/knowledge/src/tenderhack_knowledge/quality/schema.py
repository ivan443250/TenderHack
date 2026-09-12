"""Knowledge-owned tables used by the durable quality analytics layer.

The tables intentionally contain copies of facts pushed by ``api-worker``.
There are no foreign keys to support-core tables: the knowledge runtime must
remain deployable and testable without being able to select API-owned data.
"""

from __future__ import annotations

import sqlalchemy as sa


metadata = sa.MetaData()

quality_cases = sa.Table(
    "quality_cases",
    metadata,
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

quality_feedback = sa.Table(
    "quality_feedback",
    metadata,
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

quality_completions = sa.Table(
    "quality_completions",
    metadata,
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

quality_evaluations = sa.Table(
    "quality_evaluations",
    metadata,
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

issue_groups = sa.Table(
    "issue_groups",
    metadata,
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

issue_group_members = sa.Table(
    "issue_group_members",
    metadata,
    sa.Column("group_key", sa.String(512), nullable=False),
    sa.Column("delivery_key", sa.String(256), nullable=False),
    sa.Column("case_id", sa.String(256), nullable=False),
    sa.PrimaryKeyConstraint("group_key", "delivery_key"),
)

for table, columns, name in (
    (quality_cases, ("case_id", "occurred_at"), "ix_quality_cases_case_occurred"),
    (quality_cases, ("payload_hash",), "ix_quality_cases_payload_hash"),
    (quality_feedback, ("case_id", "occurred_at"), "ix_quality_feedback_case_occurred"),
    (quality_completions, ("resolution_status", "completed_at"), "ix_quality_completions_status"),
    (quality_evaluations, ("case_id", "evaluated_at"), "ix_quality_evaluations_case_evaluated"),
    (issue_groups, ("sample_size",), "ix_issue_groups_sample_size"),
    (issue_group_members, ("case_id",), "ix_issue_group_members_case"),
):
    sa.Index(name, *[table.c[column] for column in columns])


__all__ = [
    "metadata",
    "quality_cases",
    "quality_feedback",
    "quality_completions",
    "quality_evaluations",
    "issue_groups",
    "issue_group_members",
]
