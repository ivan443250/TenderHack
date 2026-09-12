from __future__ import annotations

from pathlib import Path

import pytest

from tenderhack_knowledge.ingestion import (
    BootstrapError,
    CorpusExpectation,
    CorpusIdentityMismatch,
    IngestionPipeline,
    MissingNormativeArtifact,
    bootstrap_in_memory,
)

from test_ingestion_foundation import _technical_pdf


def _manifest(tmp_path: Path) -> tuple[list[dict[str, object]], int]:
    payload = _technical_pdf([["1 Procedure", "A source instruction."], []])
    entries: list[dict[str, object]] = []
    for index in range(2):
        path = tmp_path / f"manual-{index}.pdf"
        path.write_bytes(payload)
        entries.append(
            {
                "path": str(path),
                "original_filename": f"manual-{index}.pdf",
                "source_reference": f"organizer/manual-{index}.pdf",
                "declared_version": "v1",
                "expected_pages": 2,
            }
        )
    fragments = len(IngestionPipeline().prepare(entries[0]["path"]).fragments)
    return entries, fragments * len(entries)


def test_bootstrap_is_single_snapshot_and_idempotent(tmp_path: Path) -> None:
    entries, fragment_count = _manifest(tmp_path)
    result = bootstrap_in_memory(
        entries,
        expectation=CorpusExpectation(documents=2, pages=4, fragments=fragment_count),
        cards=(),
    )

    assert result.status == "CERTIFIED_WITH_LIMITATIONS"
    assert result.document_count == 2
    assert result.pages == 4
    assert result.fragments == fragment_count
    assert result.snapshot_id
    assert result.first_bootstrap["all_versions_new"] is True
    assert result.repeated_bootstrap["all_versions_idempotent"] is True
    assert result.duplicate_count == 0
    assert result.historical_corpus_isolated is True
    assert result.source_resolution_checks


def test_bootstrap_validates_complete_identity_before_writing(tmp_path: Path) -> None:
    entries, fragment_count = _manifest(tmp_path)
    with pytest.raises(CorpusIdentityMismatch, match="expected"):
        bootstrap_in_memory(
            entries,
            expectation=CorpusExpectation(documents=6, pages=830, fragments=3899),
            cards=(),
        )
    assert fragment_count > 0


def test_bootstrap_rejects_missing_or_historical_sources(tmp_path: Path) -> None:
    missing = [
        {
            "path": str(tmp_path / "missing.pdf"),
            "original_filename": "missing.pdf",
            "source_reference": "organizer/missing.pdf",
        }
    ]
    with pytest.raises(MissingNormativeArtifact):
        bootstrap_in_memory(missing, expectation=CorpusExpectation(documents=1, pages=1, fragments=1), cards=())

    path = tmp_path / "historical.pdf"
    path.write_bytes(_technical_pdf([["historical"], []]))
    historical = [
        {
            "path": str(path),
            "original_filename": "historical.pdf",
            "source_reference": "stp/historical.pdf",
            "corpus": "HISTORICAL",
        }
    ]
    with pytest.raises(CorpusIdentityMismatch, match="cannot include"):
        bootstrap_in_memory(historical, expectation=CorpusExpectation(documents=1, pages=2, fragments=1), cards=())


def test_bootstrap_rejects_card_seed_pinned_to_another_snapshot(tmp_path: Path) -> None:
    entries, fragment_count = _manifest(tmp_path)
    from tenderhack_knowledge.conditions.cards import (
        ConditionCard,
        ConditionCardApplicability,
        ConditionCardRisk,
    )

    card = ConditionCard(
        card_id="card",
        version="1",
        snapshot_id="different-snapshot",
        title="Card",
        applicability=ConditionCardApplicability(),
        allowed_action="Use the source.",
        forbidden_generalization="Do not generalize.",
        handoff_condition="Only if source requires it.",
        risk_level=ConditionCardRisk.LOW,
        source_fragment_ids=("missing",),
        source_anchors=("page 1",),
    )
    with pytest.raises(BootstrapError, match="pinned"):
        bootstrap_in_memory(
            entries,
            expectation=CorpusExpectation(documents=2, pages=4, fragments=fragment_count),
            cards=(card,),
        )
