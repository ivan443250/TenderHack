"""Regression tests for AI-4 durable quality analytics foundation."""

from __future__ import annotations

import os
from datetime import datetime, timezone
from pathlib import Path

import pytest

from tenderhack_knowledge.contracts.v0 import (
    DimensionValue,
    FeedbackRating,
    IntegrationMode,
    QualityCompletionPush,
    QualityFeedbackPush,
    QualityTurnPush,
    StageTimings,
)
from tenderhack_knowledge.quality import (
    InMemoryQualityStore,
    QualityPayloadConflict,
    build_issue_groups,
    evaluate_turn_facts,
)


NOW = datetime(2026, 9, 12, 12, 0, tzinfo=timezone.utc)


def _turn(
    *,
    case_id: str = "case-1",
    turn_id: str = "turn-1",
    revision: int = 1,
    answer: str | None = "Use the supplier portal and open the source.",
    evidence: list[str] | None = None,
    service_need: str | None = "UPD status",
    error_category: str | None = None,
) -> QualityTurnPush:
    return QualityTurnPush(
        case_id=case_id,
        turn_id=turn_id,
        revision=revision,
        occurred_at=NOW,
        question_text="How do I submit the document?",
        decision="ANSWER",
        reason_codes=["EVIDENCE_SUFFICIENT"],
        answer_markdown=answer,
        evidence_fragment_ids=evidence,
        snapshot_id="normative-snapshot-1",
        service_need=service_need,
        stage_timings=StageTimings(understand_ms=10),
        error_category=error_category,
    )


def _feedback(feedback_id: str = "feedback-1", *, case_id: str = "case-1") -> QualityFeedbackPush:
    return QualityFeedbackPush(
        feedback_id=feedback_id,
        case_id=case_id,
        turn_id="turn-1",
        occurred_at=NOW,
        specialist_rating=FeedbackRating.POSITIVE,
        information_quality_rating=FeedbackRating.NEGATIVE,
        specialist_ref="opaque-adapter-ref",
        integration_mode=IntegrationMode.SIMULATED,
        solved=False,
        comment_text="The answer did not resolve the issue.",
    )


def _completion(case_id: str = "case-1") -> QualityCompletionPush:
    return QualityCompletionPush(
        case_id=case_id,
        completed_at=NOW,
        completion_reason="USER_CLOSED",
        resolution_status="UNRESOLVED",
        handoff_status="SIMULATED_ACCEPTED",
        integration_mode=IntegrationMode.SIMULATED,
        specialist_ref="opaque-adapter-ref",
    )


def test_first_turn_intake_is_persisted_and_has_provenance() -> None:
    store = InMemoryQualityStore()
    result = store.ingest_turn(_turn(evidence=["frag-1"]), received_at=NOW, provenance={"trace_id": "trace-1", "secret": "drop"})
    assert result.status == "stored"
    row = store.turns["turn-1:1"]
    assert row["received_at"] == NOW
    assert row["source_type"] == "api-worker"
    assert row["provenance"]["trace_id"] == "trace-1"
    assert "secret" not in row["provenance"]
    assert row["payload_hash"]


def test_exact_duplicate_turn_is_idempotent_and_conflicting_duplicate_is_explicit() -> None:
    store = InMemoryQualityStore()
    payload = _turn(evidence=["frag-1"])
    assert store.ingest_turn(payload, received_at=NOW).status == "stored"
    assert store.ingest_turn(payload, received_at=NOW).status == "idempotent"
    with pytest.raises(QualityPayloadConflict):
        store.ingest_turn(payload.model_copy(update={"answer_markdown": "different"}), received_at=NOW)


def test_feedback_and_completion_are_separate_independent_signals() -> None:
    store = InMemoryQualityStore()
    store.ingest_turn(_turn(evidence=["frag-1"]), received_at=NOW)
    store.ingest_feedback(_feedback(), received_at=NOW)
    store.ingest_completion(_completion(), received_at=NOW)
    assert store.feedback["feedback-1"]["specialist_rating"] == "POSITIVE"
    assert store.feedback["feedback-1"]["information_quality_rating"] == "NEGATIVE"
    assert store.feedback["feedback-1"]["solved"] is False
    assert store.completions["case-1"]["resolution_status"] == "UNRESOLVED"
    assert store.summary()["feedback_count"] == 1
    assert store.summary()["completion_count"] == 1


def test_feedback_duplicate_and_completion_duplicate_conflicts() -> None:
    store = InMemoryQualityStore()
    assert store.ingest_feedback(_feedback(), received_at=NOW).status == "stored"
    assert store.ingest_feedback(_feedback(), received_at=NOW).status == "idempotent"
    with pytest.raises(QualityPayloadConflict):
        store.ingest_feedback(_feedback().model_copy(update={"solved": True}), received_at=NOW)
    assert store.ingest_completion(_completion(), received_at=NOW).status == "stored"
    assert store.ingest_completion(_completion(), received_at=NOW).status == "idempotent"
    with pytest.raises(QualityPayloadConflict):
        store.ingest_completion(_completion().model_copy(update={"resolution_status": "RESOLVED"}), received_at=NOW)
    later = _completion().model_copy(update={"completed_at": NOW.replace(minute=1), "resolution_status": "RESOLVED"})
    assert store.ingest_completion(later, received_at=NOW).status == "updated"
    assert store.completions["case-1"]["resolution_status"] == "RESOLVED"


def test_evaluator_with_evidence_is_conservative_and_explainable() -> None:
    result = evaluate_turn_facts(_turn(evidence=["frag-1", "frag-2"]), evaluated_at=NOW)
    assert result.factual_support["value"] == DimensionValue.ONE.value
    assert result.factual_support["evidence"] == "frag-1, frag-2"
    assert result.factual_support["limitation"]
    assert result.completeness["value"] == DimensionValue.ONE.value
    assert result.clarity["value"] == DimensionValue.TWO.value
    assert result.next_step["value"] == DimensionValue.TWO.value
    assert result.critical_error is False


def test_evaluator_without_evidence_never_claims_factual_correctness() -> None:
    result = evaluate_turn_facts(_turn(evidence=None), evaluated_at=NOW)
    assert result.factual_support["value"] == DimensionValue.UNKNOWN.value
    assert result.completeness["value"] == DimensionValue.UNKNOWN.value
    assert result.clarity["value"] == DimensionValue.TWO.value
    assert result.factual_support["reason"]


def test_evaluator_preserves_not_applicable_and_unknown_as_distinct_from_zero() -> None:
    no_answer = evaluate_turn_facts(_turn(answer=None, evidence=None), evaluated_at=NOW)
    assert no_answer.clarity["value"] == DimensionValue.NOT_APPLICABLE.value
    assert no_answer.next_step["value"] == DimensionValue.NOT_APPLICABLE.value
    unknown = evaluate_turn_facts(_turn(answer="an answer", evidence=None), evaluated_at=NOW)
    assert unknown.factual_support["value"] == DimensionValue.UNKNOWN.value
    assert unknown.factual_support["value"] not in {DimensionValue.ZERO.value, DimensionValue.NOT_APPLICABLE.value}


def test_critical_error_is_independent_of_dimension_values() -> None:
    result = evaluate_turn_facts(_turn(evidence=None, error_category="TIMEOUT"), evaluated_at=NOW)
    assert result.critical_error is True
    assert result.critical_error_reason == "Pushed error category: TIMEOUT"
    assert result.factual_support["value"] == DimensionValue.UNKNOWN.value


def test_store_evaluation_and_query_are_deterministic() -> None:
    store = InMemoryQualityStore()
    store.ingest_turn(_turn(evidence=["frag-1"]), received_at=NOW)
    first = store.evaluate_turn("turn-1", 1)
    second = store.evaluate_turn("turn-1", 1)
    assert first == second
    assert [item.turn_id for item in store.evaluation_query("case-1")] == ["turn-1"]
    assert first.to_contract().turn_id == "turn-1"


def test_issue_groups_use_only_supplied_attributes_and_have_limitations() -> None:
    turns = [
        _turn(case_id="case-2", turn_id="turn-2", evidence=["frag-2"]).model_dump(mode="json"),
        _turn(case_id="case-1", turn_id="turn-1", evidence=["frag-1"]).model_dump(mode="json"),
        _turn(case_id="case-3", turn_id="turn-3", evidence=["frag-3"]).model_dump(mode="json"),
    ]
    feedback = [_feedback("feedback-1", case_id="case-1").model_dump(mode="json")]
    completions = [_completion("case-1").model_dump(mode="json")]
    groups = build_issue_groups(turns, feedback, completions)
    assert len(groups) == 1
    group = groups[0]
    assert group.sample_size == 3
    assert group.representative_case_ids == ("case-1", "case-2", "case-3")
    assert group.negative_signal_count == 1
    assert group.unresolved_count == 1
    assert all("hypothesis" in item["text"].lower() for item in group.hypotheses)
    assert group.limitations
    assert "rank" not in json_keys(group)


def json_keys(group: object) -> set[str]:
    """Keep the employee-safety assertion independent from implementation details."""
    return set(getattr(group, "__dict__", {}))


def test_no_employee_leaderboard_or_normative_projection_is_created() -> None:
    store = InMemoryQualityStore()
    store.ingest_turn(_turn(evidence=["frag-1"]), received_at=NOW)
    store.ingest_feedback(_feedback(), received_at=NOW)
    store.rebuild_issue_groups()
    assert not hasattr(store, "employee_ranking")
    assert all("specialist" not in group.label.casefold() for group in store.groups)
    summary_text = str(store.summary()).casefold()
    assert "leaderboard" not in summary_text
    assert "employee score" not in summary_text
    assert "best specialist" not in summary_text


def test_grouping_is_stable_for_input_order_and_does_not_promote_history() -> None:
    first = build_issue_groups([_turn(case_id="b", turn_id="tb").model_dump(mode="json"), _turn(case_id="a", turn_id="ta").model_dump(mode="json")])
    second = build_issue_groups([_turn(case_id="a", turn_id="ta").model_dump(mode="json"), _turn(case_id="b", turn_id="tb").model_dump(mode="json")])
    assert first == second
    # A historical resolution label is still just an opaque decision label;
    # the analytics layer never exposes it as normative source material.
    assert all("normative" not in group.label.casefold() for group in first)


def test_migration_is_single_0008_head_and_has_no_foreign_keys() -> None:
    migration = Path(__file__).parents[1] / "migrations" / "versions" / "0008_quality_analytics.py"
    source = migration.read_text(encoding="utf-8")
    assert 'revision = "0008_quality_analytics"' in source
    assert 'down_revision = "0007_giga_2048_embeddings"' in source
    assert "ForeignKey" not in source
    assert source.count("def upgrade") == 1
    assert source.count("def downgrade") == 1
    assert len(list((migration.parent).glob("0008_*quality*.py"))) == 1


@pytest.mark.asyncio
async def test_real_postgres_quality_roundtrip_when_explicitly_configured() -> None:
    url = os.getenv("AI4_TEST_DATABASE_URL", "")
    if not url:
        pytest.skip("AI4_TEST_DATABASE_URL is not configured")
    # The disposable database is intentionally opt-in.  Running against the
    # normal development DB would violate the AI-4 safety requirements.
    from sqlalchemy.ext.asyncio import create_async_engine

    from tenderhack_knowledge.quality import QualityRepository

    engine = create_async_engine(url, pool_pre_ping=True)
    try:
        repository = QualityRepository(engine)
        first = await repository.ingest_turn(_turn(evidence=["frag-1"]), received_at=NOW)
        duplicate = await repository.ingest_turn(_turn(evidence=["frag-1"]), received_at=NOW)
        assert first.status == "stored"
        assert duplicate.status == "idempotent"
        evaluation = await repository.evaluate_turn("turn-1", 1)
        assert evaluation.factual_support["value"] == DimensionValue.ONE.value
    finally:
        await engine.dispose()
