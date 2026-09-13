import json
import os
from datetime import datetime, timezone
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from sqlalchemy.ext.asyncio import create_async_engine

from tenderhack_knowledge.answerability import assess_answerability
from tenderhack_knowledge.api import routes
from tenderhack_knowledge.conditions.cards import ConditionCard, ConditionCardApplicability, ConditionCardRisk
from tenderhack_knowledge.contracts.v0 import Corpus, EvidenceSufficiency
from tenderhack_knowledge.ingestion.models import KnowledgeFragment, KnowledgeSnapshot, SourceAnchor
from tenderhack_knowledge.ingestion.repository import CorpusBoundaryError
from tenderhack_knowledge.main import app
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository


SNAPSHOT_ID = "snap-test"


def _fragment(fragment_id: str, text: str, *, snapshot_id: str | None = SNAPSHOT_ID) -> KnowledgeFragment:
    return KnowledgeFragment(
        fragment_id=fragment_id,
        document_version_id="doc-version-test",
        snapshot_id=snapshot_id,
        page_start=1,
        page_end=1,
        section="Test source",
        kind="text",
        text=text,
        source_anchor=SourceAnchor(page=1, section="Test source", text_hash=f"hash-{fragment_id}"),
        review_status="VERIFIED",
        text_hash=f"hash-{fragment_id}",
    )


def _card(
    fragment_id: str,
    *,
    card_id: str = "card-test",
    version: str = "1",
    risk: ConditionCardRisk = ConditionCardRisk.MEDIUM,
    required_slots: tuple[str, ...] = (),
    roles: tuple[str, ...] = ("supplier",),
    process: tuple[str, ...] = ("UPD execution",),
    provider: tuple[str, ...] = (),
) -> ConditionCard:
    return ConditionCard(
        card_id=card_id,
        version=version,
        snapshot_id=SNAPSHOT_ID,
        title="Test condition card",
        applicability=ConditionCardApplicability(roles=roles, process=process, provider=provider),
        required_slots=required_slots,
        allowed_action="Use the cited source only",
        forbidden_generalization="Do not generalize outside the declared context",
        handoff_condition="No handoff is established by this test card",
        risk_level=risk,
        source_fragment_ids=(fragment_id,),
        source_anchors=("page:1",),
        review_status="VERIFIED",
    )


class _Repository:
    def __init__(self, fragments: tuple[KnowledgeFragment, ...], cards: tuple[ConditionCard, ...] = ()) -> None:
        self.fragments = fragments
        self.cards = cards
        self.snapshot = KnowledgeSnapshot(
            snapshot_id=SNAPSHOT_ID,
            corpus=Corpus.NORMATIVE,
            document_version_ids=("doc-version-test",),
            manifest_hash="manifest-test",
            created_at=datetime.now(timezone.utc),
            review_status="VERIFIED",
        )

    def get_snapshot(self, snapshot_id: str) -> KnowledgeSnapshot | None:
        return self.snapshot if snapshot_id == SNAPSHOT_ID else None

    def snapshot_fragments(self, snapshot_id: str) -> tuple[KnowledgeFragment, ...]:
        return self.fragments if snapshot_id == SNAPSHOT_ID else ()

    def condition_cards(self, snapshot_id: str) -> tuple[ConditionCard, ...]:
        return self.cards if snapshot_id == SNAPSHOT_ID else ()


@pytest.mark.asyncio
async def test_clear_evidence_is_sufficient_and_contract_projection_has_only_frozen_fields() -> None:
    fragment = _fragment("f-code", "Ошибка РДИК_0059 при формировании УПД в ЕИС.")
    card = _card("f-code", required_slots=("error_code",), provider=("EIS",), risk=ConditionCardRisk.HIGH)
    assessment = await assess_answerability(
        "Поставщик в ЕИС получил РДИК_0059 при формировании УПД",
        SNAPSHOT_ID,
        [fragment.fragment_id],
        _Repository((fragment,), (card,)),
    )
    assert assessment.evidence_sufficiency is EvidenceSufficiency.SUFFICIENT
    assert assessment.evidence_fragment_ids == (fragment.fragment_id,)
    payload = assessment.to_contract().model_dump()
    assert set(payload) == {"evidence_sufficiency", "evidence_fragment_ids", "missing_conditions", "risk_flags"}
    assert not {"Decision", "should_handoff", "HandoffStatus", "ResolutionStatus"}.intersection(payload)


@pytest.mark.asyncio
async def test_missing_required_slot_is_condition_dependent_not_insufficient() -> None:
    fragment = _fragment("f-duration", "Если статус УПД не обновляется более часа, обратитесь в СТП ПП.")
    card = _card("f-duration", required_slots=("duration",), risk=ConditionCardRisk.HIGH)
    assessment = await assess_answerability(
        "Поставщик: статус УПД не обновляется",
        SNAPSHOT_ID,
        [fragment.fragment_id],
        _Repository((fragment,), (card,)),
    )
    assert assessment.evidence_sufficiency is EvidenceSufficiency.CONDITION_DEPENDENT
    assert "duration" in assessment.missing_conditions
    assert "HUMAN_SUPPORT_REQUIRED" not in assessment.risk_flags
    assert "HIGH_RISK_MISSING_CONDITION" in assessment.risk_flags


@pytest.mark.asyncio
async def test_threshold_support_branch_requires_human_need_only_after_duration_is_known() -> None:
    fragment = _fragment("f-duration", "Если статус УПД не обновляется более часа, обратитесь в СТП ПП.")
    card = _card("f-duration", required_slots=("duration",), risk=ConditionCardRisk.HIGH)
    repository = _Repository((fragment,), (card,))
    assessment = await assess_answerability("Поставщик: статус УПД не обновляется более часа", SNAPSHOT_ID, ["f-duration"], repository)
    assert assessment.evidence_sufficiency is EvidenceSufficiency.INSUFFICIENT
    assert "HUMAN_SUPPORT_REQUIRED" in assessment.risk_flags


@pytest.mark.asyncio
async def test_wrong_provider_and_forbidden_generalization_are_insufficient() -> None:
    fragment = _fragment("f-provider", "Ошибка РДИК_0060 при формировании УПД в ЕИС.")
    card = _card("f-provider", required_slots=("error_code",), provider=("EIS",), risk=ConditionCardRisk.HIGH)
    assessment = await assess_answerability(
        "Поставщик в Портале заказчика получил РДИК_0060 при формировании УПД",
        SNAPSHOT_ID,
        ["f-provider"],
        _Repository((fragment,), (card,)),
    )
    assert assessment.evidence_sufficiency is EvidenceSufficiency.INSUFFICIENT
    assert "WRONG_PROVIDER" in assessment.risk_flags
    assert "FORBIDDEN_GENERALIZATION" in assessment.risk_flags


@pytest.mark.asyncio
async def test_no_evidence_and_historical_snapshot_are_rejected() -> None:
    repository = _Repository((_fragment("f1", "Нормативное описание УПД."),))
    empty = await assess_answerability("Как настроить оплату?", SNAPSHOT_ID, [], repository)
    assert empty.evidence_sufficiency is EvidenceSufficiency.INSUFFICIENT
    assert "MISSING_SOURCE" in empty.risk_flags

    historical = repository.snapshot.model_copy(update={"corpus": Corpus.HISTORICAL})
    repository.snapshot = historical
    with pytest.raises(CorpusBoundaryError):
        await assess_answerability("УПД", SNAPSHOT_ID, ["f1"], repository)


@pytest.mark.asyncio
async def test_partial_invalid_candidate_set_cannot_be_answered() -> None:
    fragment = _fragment("f-valid", "Поставщику выбрать тип документа УПД при создании исполнения.")
    assessment = await assess_answerability(
        "Поставщику выбрать тип документа УПД",
        SNAPSHOT_ID,
        ["f-valid", "f-not-in-snapshot"],
        _Repository((fragment,)),
    )
    assert assessment.evidence_sufficiency is EvidenceSufficiency.INSUFFICIENT
    assert assessment.evidence_fragment_ids == ("f-valid",)
    assert "INVALID_EVIDENCE_REFERENCE" in assessment.risk_flags


@pytest.mark.asyncio
async def test_top10_candidates_are_reduced_to_relevant_evidence_before_gate() -> None:
    relevant = _fragment("f-relevant", "UPD signing requires CryptoPro CSP and a supported browser.")
    distractors = tuple(
        _fragment(f"f-distractor-{index}", "Customer portal document fields and delivery address.")
        for index in range(6)
    )
    assessment = await assess_answerability(
        "UPD signing with CryptoPro",
        SNAPSHOT_ID,
        [relevant.fragment_id, *(fragment.fragment_id for fragment in distractors)],
        _Repository((relevant, *distractors)),
    )
    assert assessment.evidence_sufficiency is EvidenceSufficiency.SUFFICIENT
    assert assessment.evidence_fragment_ids == (relevant.fragment_id,)
    assert "ROLE_AMBIGUITY" not in assessment.risk_flags


@pytest.mark.asyncio
async def test_role_mention_in_one_context_fragment_is_not_role_ambiguity() -> None:
    fragment = _fragment(
        "f-offer",
        "Поставщик создаёт СТЕ для оферты, после чего заказчик получает предложение.",
    )
    assessment = await assess_answerability(
        "Как создать СТЕ для оферты?",
        SNAPSHOT_ID,
        [fragment.fragment_id],
        _Repository((fragment,)),
    )

    assert assessment.evidence_sufficiency is EvidenceSufficiency.SUFFICIENT
    assert "ROLE_AMBIGUITY" not in assessment.risk_flags


@pytest.mark.asyncio
async def test_independent_supplier_and_customer_fragments_remain_ambiguous() -> None:
    supplier = _fragment("f-supplier", "Поставщик создаёт СТЕ для оферты.")
    customer = _fragment("f-customer", "Заказчик утверждает СТЕ для оферты.")
    assessment = await assess_answerability(
        "Как создать СТЕ для оферты?",
        SNAPSHOT_ID,
        [supplier.fragment_id, customer.fragment_id],
        _Repository((supplier, customer)),
    )

    assert assessment.evidence_sufficiency is EvidenceSufficiency.INSUFFICIENT
    assert "ROLE_AMBIGUITY" in assessment.risk_flags


@pytest.mark.asyncio
async def test_simple_inability_is_not_post_instruction_failure_but_explicit_retry_is() -> None:
    fragment = _fragment("f-signing", "Для подписания УПД требуется КриптоПро CSP.")
    repository = _Repository((fragment,))
    initial = await assess_answerability(
        "Не получается подписать УПД через КриптоПро",
        SNAPSHOT_ID,
        [fragment.fragment_id],
        repository,
    )
    assert initial.evidence_sufficiency is EvidenceSufficiency.SUFFICIENT
    assert "POST_INSTRUCTION_FAILURE" not in initial.risk_flags

    retried = await assess_answerability(
        "Я уже попробовал подписать УПД, но не помогло",
        SNAPSHOT_ID,
        [fragment.fragment_id],
        repository,
    )
    assert retried.evidence_sufficiency is EvidenceSufficiency.INSUFFICIENT
    assert "POST_INSTRUCTION_FAILURE" in retried.risk_flags


@pytest.mark.asyncio
async def test_latest_card_version_supersedes_old_required_slots_without_mutating_storage() -> None:
    fragment = _fragment("f-version", "Поставщику выбрать тип документа УПД при создании исполнения.")
    old = _card("f-version", card_id="versioned", version="1", required_slots=("duration",), process=("contract execution",))
    new = _card("f-version", card_id="versioned", version="2", required_slots=(), process=("contract execution",))
    assessment = await assess_answerability(
        "Поставщику выбрать тип документа УПД при создании исполнения",
        SNAPSHOT_ID,
        ["f-version"],
        _Repository((fragment,), (old, new)),
    )
    assert assessment.evidence_sufficiency is EvidenceSufficiency.SUFFICIENT
    assert old.version == "1" and new.version == "2"


def test_fixture_contains_at_least_thirty_grounded_deterministic_cases() -> None:
    path = Path(__file__).parents[1] / "benchmarks" / "k4-answerability-fixture.json"
    fixture = json.loads(path.read_text(encoding="utf-8"))
    assert len(fixture["cases"]) >= 30
    assert {case["expected"] for case in fixture["cases"]} == {
        "SUFFICIENT",
        "CONDITION_DEPENDENT",
        "INSUFFICIENT",
    }
    for case in fixture["cases"]:
        assert case["candidate_fragment_ids"] or case["expected"] == "INSUFFICIENT"
        assert all(value.startswith("frag_") or value == "fragment-does-not-exist" for value in case["candidate_fragment_ids"])


def test_answerability_route_uses_evidence_gate_without_contract_drift(monkeypatch: pytest.MonkeyPatch) -> None:
    fragment = _fragment("f-route", "Поставщику выбрать тип документа УПД при создании исполнения.")
    repository = _Repository((fragment,))
    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: repository)
    headers = {
        "X-Trace-Id": "00000000-0000-4000-8000-000000000000",
        "X-Case-Id": "case-k4",
        "X-Turn-Id": "turn-k4",
    }
    with TestClient(app) as client:
        response = client.post(
            "/v0/answerability",
            headers=headers,
            json={"query": "Поставщику выбрать тип документа УПД", "snapshot_id": SNAPSHOT_ID, "candidate_fragment_ids": ["f-route"]},
        )
    assert response.status_code == 200
    body = response.json()
    assert body["evidence_sufficiency"] == "SUFFICIENT"
    assert set(body) == {"evidence_sufficiency", "evidence_fragment_ids", "missing_conditions", "risk_flags"}


@pytest.mark.asyncio
async def test_real_postgres_fixture_has_no_false_sufficient_when_configured() -> None:
    url = os.getenv("K4_TEST_DATABASE_URL", "")
    if not url:
        pytest.skip("K4_TEST_DATABASE_URL is not configured")
    fixture_path = Path(__file__).parents[1] / "benchmarks" / "k4-answerability-fixture.json"
    fixture = json.loads(fixture_path.read_text(encoding="utf-8"))
    engine = create_async_engine(url, pool_pre_ping=True)
    try:
        repository = PostgresKnowledgeRepository(engine)
        results = []
        for case in fixture["cases"]:
            assessment = await assess_answerability(
                case["query"], fixture["snapshot_id"], case["candidate_fragment_ids"], repository
            )
            results.append((case["expected"], assessment.evidence_sufficiency.value))
        assert len(results) >= 30
        assert not any(expected != "SUFFICIENT" and actual == "SUFFICIENT" for expected, actual in results)
    finally:
        await engine.dispose()
