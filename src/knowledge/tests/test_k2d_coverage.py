from __future__ import annotations

from types import SimpleNamespace

from tenderhack_knowledge.retrieval.coverage_k2d import (
    classify_observation,
    deterministic_stratified_sample,
    evaluation_query,
    query_sha256,
)


def _record(row: int, query: str, mapped_theme: str = "A") -> dict[str, object]:
    return {
        "row": row,
        "query": query,
        "query_sha256": query_sha256(query),
        "mapped_theme": mapped_theme,
        "length_bucket": "medium_31_120",
        "technical": True,
        "ambiguous": False,
        "historical_action_likely": False,
    }


def test_evaluation_query_removes_only_known_export_prefix() -> None:
    assert evaluation_query(" Подтема запроса:  Как подписать УПД? ") == "Как подписать УПД?"
    assert evaluation_query("Подтема: оставить этот текст") == "Подтема: оставить этот текст"


def test_sample_is_deterministic_and_covers_available_groups() -> None:
    records = [_record(index, f"query {index}", mapped_theme=theme) for index, theme in enumerate("ABC")]
    first = deterministic_stratified_sample(records, target=3, seed=7)
    second = deterministic_stratified_sample(records, target=3, seed=7)
    assert [item["query_sha256"] for item in first] == [item["query_sha256"] for item in second]
    assert {item["mapped_theme"] for item in first} == {"A", "B", "C"}


def test_sample_deduplicates_normalized_query() -> None:
    records = [_record(1, "Подтема запроса: УПД"), _record(2, " упд ")]
    sample = deterministic_stratified_sample(records, target=10)
    assert len(sample) == 1


def test_classification_keeps_historical_signal_separate_from_evidence() -> None:
    candidate = SimpleNamespace(
        text="Порядок подписания УПД",
        channels=("fts",),
        candidate=SimpleNamespace(
            fragment_id="frag-1",
            scores=SimpleNamespace(model_dump=lambda: {"fts": 0.7}),
        ),
    )
    result = SimpleNamespace(records=(candidate,))
    classification, signals = classify_observation(_record(1, "Как подписать УПД?"), result)
    assert classification == "EVIDENCE_POSSIBLE"
    assert signals["historical_action_signal"] is False

    historical = _record(2, "Как подписать УПД?")
    historical["historical_action_likely"] = True
    classification, signals = classify_observation(historical, result)
    assert classification == "HUMAN_ACTION_LIKELY"
    assert signals["signal"] == "historical_solution_coarse_signal"


def test_classification_does_not_call_a_score_alone_evidence() -> None:
    candidate = SimpleNamespace(
        text="Совершенно другой текст",
        channels=("trigram",),
        candidate=SimpleNamespace(
            fragment_id="frag-2",
            scores=SimpleNamespace(model_dump=lambda: {"fts": None}),
        ),
    )
    classification, signals = classify_observation(
        _record(3, "Как подписать УПД?"), SimpleNamespace(records=(candidate,))
    )
    assert classification == "UNASSESSABLE"
    assert signals["signal"] == "candidate_without_conservative_text_correspondence"
