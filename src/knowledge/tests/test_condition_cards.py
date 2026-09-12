from __future__ import annotations

from datetime import datetime, timezone

import pytest

from tenderhack_knowledge.conditions import (
    CardMatchContext,
    CardMatchReason,
    ConditionCard,
    ConditionCardApplicability,
    ConditionCardRisk,
    InMemoryConditionCardStore,
    augment_with_cards,
    match_card,
    verify_condition_cards,
)
from tenderhack_knowledge.ingestion.models import (
    Corpus,
    Document,
    DocumentVersion,
    IngestionRun,
    KnowledgeFragment,
    SourceAnchor,
)
from tenderhack_knowledge.ingestion.repository import InMemoryKnowledgeRepository


def _fixture(*, corpus: Corpus = Corpus.NORMATIVE) -> tuple[InMemoryKnowledgeRepository, str, KnowledgeFragment]:
    repository = InMemoryKnowledgeRepository()
    now = datetime.now(timezone.utc)
    document = Document(document_id=f"doc-{corpus.value.lower()}", original_filename="manual.pdf", corpus=corpus)
    version = DocumentVersion(
        document_version_id=f"ver-{corpus.value.lower()}",
        document_id=document.document_id,
        content_sha256="a" * 64,
        page_count=1,
        source_reference="manual.pdf",
        ingested_at=now,
    )
    fragment = KnowledgeFragment(
        fragment_id=f"frag-{corpus.value.lower()}",
        document_version_id=version.document_version_id,
        page_start=1,
        page_end=1,
        kind="paragraph",
        text="Источник требует указать статус и обратиться в поддержку после порога.",
        source_anchor=SourceAnchor(page=1, text_hash="b" * 64),
        text_hash="b" * 64,
    )
    run = IngestionRun(
        run_id=f"run-{corpus.value.lower()}",
        corpus=corpus,
        status="COMPLETED",
        source_count=1,
        document_version_ids=(version.document_version_id,),
        started_at=now,
        completed_at=now,
    )
    repository.register_document(document)
    repository.store_version(version, (fragment,), run)
    snapshot = repository.publish_snapshot((version.document_version_id,), corpus)
    return repository, snapshot.snapshot_id, fragment


def _card(snapshot_id: str, fragment_id: str, **overrides: object) -> ConditionCard:
    values: dict[str, object] = {
        "card_id": "card-test",
        "version": "1",
        "snapshot_id": snapshot_id,
        "title": "Test evidence condition",
        "applicability": ConditionCardApplicability(
            roles=("supplier",),
            process=("UPD",),
            provider=("EIS",),
            document_status=("Черновик",),
            required_conditions=("source is present",),
        ),
        "required_slots": ("error_code",),
        "allowed_action": "Use the source instruction.",
        "forbidden_generalization": "Do not generalize to another provider or status.",
        "handoff_condition": "Only when the source explicitly says so.",
        "risk_level": ConditionCardRisk.HIGH,
        "source_fragment_ids": (fragment_id,),
        "source_quotes": ("Источник требует",),
        "source_anchors": ("page 1",),
        "review_status": "VERIFIED",
    }
    values.update(overrides)
    return ConditionCard.model_validate(values)


def test_applicable_card_and_missing_slot_are_deterministic() -> None:
    card = _card("snap", "frag")
    context = CardMatchContext(
        role="supplier",
        process="UPD",
        provider="EIS",
        document_status="Черновик",
        error_code="RDIK_0060",
    )
    result = match_card(card, context)
    assert result.applicable is True
    assert result.score == 1.0

    missing = match_card(card, context.model_copy(update={"error_code": None}))
    assert missing.applicable is False
    assert "error_code" in missing.missing_required_slots
    assert CardMatchReason.MISSING_REQUIRED_SLOT in missing.reasons


def test_wrong_provider_status_and_forbidden_generalization_are_rejected() -> None:
    card = _card("snap", "frag")
    result = match_card(
        card,
        CardMatchContext(
            role="supplier",
            process="UPD",
            provider="other",
            document_status="Опубликован",
            error_code="RDIK_0060",
        ),
    )
    assert result.applicable is False
    assert CardMatchReason.WRONG_PROVIDER in result.reasons
    assert CardMatchReason.WRONG_STATUS in result.reasons
    assert CardMatchReason.FORBIDDEN_GENERALIZATION in result.reasons


@pytest.mark.asyncio
async def test_audit_proves_fragment_source_and_normative_snapshot() -> None:
    repository, snapshot_id, fragment = _fixture()
    audit = await verify_condition_cards((_card(snapshot_id, fragment.fragment_id),), repository, snapshot_id)
    assert audit.passed
    assert audit.verified_cards == ("card-test@1",)
    assert not audit.errors


@pytest.mark.asyncio
async def test_missing_fragment_and_historical_fragment_are_rejected() -> None:
    repository, snapshot_id, fragment = _fixture()
    missing = await verify_condition_cards((_card(snapshot_id, "missing-fragment"),), repository, snapshot_id)
    assert not missing.passed
    assert any(error.code == "MISSING_SOURCE_FRAGMENT" for error in missing.errors)

    historical_repo, historical_snapshot, historical_fragment = _fixture(corpus=Corpus.HISTORICAL)
    historical = await verify_condition_cards(
        (_card(historical_snapshot, historical_fragment.fragment_id),),
        historical_repo,
        historical_snapshot,
    )
    assert not historical.passed
    assert any(error.code == "HISTORICAL_SNAPSHOT_REJECTED" for error in historical.errors)


def test_old_and_new_card_versions_are_immutable() -> None:
    store = InMemoryConditionCardStore()
    old = _card("snap", "frag")
    new = old.model_copy(update={"version": "2", "title": "New source wording"})
    assert store.put(old) == old
    assert store.put(new) == new
    assert store.get("card-test", "1") == old
    assert store.get("card-test", "2") == new
    with pytest.raises(ValueError):
        store.put(old.model_copy(update={"title": "collision"}))


def test_high_risk_card_does_not_hide_raw_retrieval_candidates() -> None:
    card = _card("snap", "frag")
    context = CardMatchContext(
        role="supplier", process="UPD", provider="EIS", document_status="Черновик", error_code="RDIK_0060"
    )
    raw = ("raw-fragment",)
    augmented = augment_with_cards(raw, (card,), context)
    assert augmented.raw_candidates == raw
    assert augmented.condition_cards[0].match.applicable
    assert augmented.condition_cards[0].card.risk_level == ConditionCardRisk.HIGH

    no_match = augment_with_cards(raw, (card,), context.model_copy(update={"provider": "other"}))
    assert no_match.raw_candidates == raw
    assert no_match.condition_cards == ()
