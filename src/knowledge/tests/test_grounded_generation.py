from __future__ import annotations

import json
import os
from datetime import datetime, timezone
from pathlib import Path
from collections.abc import Mapping
from typing import Any

import pytest
from fastapi.testclient import TestClient
from sqlalchemy.ext.asyncio import create_async_engine

from tenderhack_knowledge.api import routes
from tenderhack_knowledge.contracts.v0 import Corpus, DraftConstraints, DraftRequest, Claim, EvidenceSufficiency
from tenderhack_knowledge.generation.models import ModelOutputError, parse_model_output
from tenderhack_knowledge.generation.service import GeneratorModelError, create_grounded_draft
from tenderhack_knowledge.ingestion.models import KnowledgeFragment, KnowledgeSnapshot, SourceAnchor
from tenderhack_knowledge.inference.errors import GeneratorClientError
from tenderhack_knowledge.inference.prompts import DRAFT_PROMPT_VERSION, VERIFY_PROMPT_VERSION, build_draft_prompt, build_verify_prompt
from tenderhack_knowledge.ingestion.repository import CorpusBoundaryError
from tenderhack_knowledge.main import app
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository
from tenderhack_knowledge.verification import verify_claims


SNAPSHOT_ID = "snap-grounded"
OTHER_SNAPSHOT_ID = "snap-other"
TRACE_HEADERS = {
    "X-Trace-Id": "00000000-0000-4000-8000-000000000000",
    "X-Case-Id": "case-grounded",
    "X-Turn-Id": "turn-grounded",
}


def _fragment(fragment_id: str, text: str, *, snapshot_id: str = SNAPSHOT_ID) -> KnowledgeFragment:
    return KnowledgeFragment(
        fragment_id=fragment_id,
        document_version_id=f"version-{snapshot_id}",
        snapshot_id=snapshot_id,
        page_start=1,
        page_end=1,
        section="Procedure",
        kind="text",
        text=text,
        source_anchor=SourceAnchor(page=1, section="Procedure", text_hash=f"hash-{fragment_id}"),
        review_status="VERIFIED",
        text_hash=f"hash-{fragment_id}",
    )


class Repository:
    def __init__(self, fragments: tuple[KnowledgeFragment, ...], *, corpus: Corpus = Corpus.NORMATIVE) -> None:
        self.fragments = fragments
        self.snapshot = KnowledgeSnapshot(
            snapshot_id=SNAPSHOT_ID,
            corpus=corpus,
            document_version_ids=("version-snap-grounded",),
            manifest_hash="manifest-grounded",
            created_at=datetime.now(timezone.utc),
            review_status="VERIFIED",
        )

    def get_snapshot(self, snapshot_id: str) -> KnowledgeSnapshot | None:
        return self.snapshot if snapshot_id == SNAPSHOT_ID else None

    def snapshot_fragments(self, snapshot_id: str) -> tuple[KnowledgeFragment, ...]:
        return self.fragments if snapshot_id == SNAPSHOT_ID else ()


class FakeGenerator:
    model_id = "fake-generator"

    def __init__(self, output: str) -> None:
        self.output = output
        self.prompts: list[str] = []
        self.response_formats: list[Mapping[str, object] | None] = []
        self.max_tokens: list[int | None] = []

    @property
    def metadata(self) -> dict[str, str]:
        return {"model_id": self.model_id, "revision": "fixture"}

    async def draft(
        self,
        prompt: str,
        *,
        response_format: Mapping[str, object] | None = None,
        max_tokens: int | None = None,
    ) -> str:
        self.prompts.append(prompt)
        self.response_formats.append(response_format)
        self.max_tokens.append(max_tokens)
        return self.output


class UnavailableGenerator(FakeGenerator):
    async def draft(
        self,
        prompt: str,
        *,
        response_format: Mapping[str, object] | None = None,
        max_tokens: int | None = None,
    ) -> str:
        self.prompts.append(prompt)
        self.response_formats.append(response_format)
        self.max_tokens.append(max_tokens)
        raise GeneratorClientError("vLLM request failed at http://inference:8000/v1/completions")


def _payload(
    fragment_ids: list[str],
    query: str = "Как сформировать УПД, выбрав тип документа?",
    *,
    max_output_tokens: int = 800,
) -> DraftRequest:
    return DraftRequest(
        query=query,
        snapshot_id=SNAPSHOT_ID,
        evidence_fragment_ids=fragment_ids,
        constraints=DraftConstraints(max_output_tokens=max_output_tokens, tone="neutral"),
    )


@pytest.mark.asyncio
async def test_grounded_draft_uses_fake_generator_and_projects_only_frozen_claims() -> None:
    fragment = _fragment("frag_102f51ec7f96e02437aa104b5136d456", "Чтобы сформировать УПД, выберите тип документа и нажмите кнопку Сформировать.")
    generator = FakeGenerator(
        '{"draft_markdown":"Выберите тип документа УПД и нажмите кнопку Сформировать.","claims":[{"claim_id":"c1","text":"Выберите тип документа УПД и нажмите кнопку Сформировать.","fragment_ids":["frag_102f51ec7f96e02437aa104b5136d456"]}]}'
    )
    result = await create_grounded_draft(_payload([fragment.fragment_id]), Repository((fragment,)), generator)
    assert result.answerability is EvidenceSufficiency.SUFFICIENT
    assert result.response.claims[0].fragment_ids == [fragment.fragment_id]
    assert "applies_if" not in result.response.claims[0].model_dump()
    assert result.response.model_version.startswith(f"{DRAFT_PROMPT_VERSION}:fake-generator@fixture")
    assert generator.prompts and DRAFT_PROMPT_VERSION in generator.prompts[0]
    assert generator.max_tokens == [800]
    assert len(generator.response_formats) == 1
    response_format = generator.response_formats[0]
    assert response_format is not None
    assert response_format["type"] == "json_object"
    schema = response_format["schema"]
    assert isinstance(schema, Mapping)
    assert schema["type"] == "object"
    assert schema["required"] == ["draft_markdown", "claims"]
    assert schema["additionalProperties"] is False
    properties = schema["properties"]
    assert isinstance(properties, Mapping)
    assert set(properties) == {"draft_markdown", "claims"}
    claims = properties["claims"]
    assert isinstance(claims, Mapping)
    claim_items = claims["items"]
    assert isinstance(claim_items, Mapping)
    claim_properties = claim_items["properties"]
    assert isinstance(claim_properties, Mapping)
    assert set(claim_properties) == {"claim_id", "text", "fragment_ids", "applies_if", "requires_human_check"}
    assert claim_items["required"] == ["claim_id", "text", "fragment_ids"]
    assert claim_items["additionalProperties"] is False


@pytest.mark.asyncio
async def test_grounded_draft_passes_explicit_lower_output_budget() -> None:
    fragment = _fragment("frag-budget", "Выберите тип документа УПД и нажмите кнопку Сформировать.")
    generator = FakeGenerator(
        '{"draft_markdown":"Выберите тип документа УПД.","claims":[{"claim_id":"c1","text":"Выберите тип документа УПД.","fragment_ids":["frag-budget"]}]}'
    )
    await create_grounded_draft(
        _payload([fragment.fragment_id], max_output_tokens=300),
        Repository((fragment,)),
        generator,
    )
    assert generator.max_tokens == [300]


@pytest.mark.asyncio
async def test_grounded_draft_rejects_output_budget_outside_hard_cap() -> None:
    fragment = _fragment("frag-budget-invalid", "Выберите тип документа УПД.")
    generator = FakeGenerator('{"draft_markdown":"x","claims":[]}')
    for value in (0, 801):
        with pytest.raises(GeneratorModelError, match="between 1 and 800"):
            await create_grounded_draft(
                _payload([fragment.fragment_id], max_output_tokens=value),
                Repository((fragment,)),
                generator,
            )
    assert generator.max_tokens == []


@pytest.mark.asyncio
async def test_insufficient_answerability_never_calls_generator_or_emits_substantive_text() -> None:
    fragment = _fragment("frag-no", "Инструкция описывает импорт каталога.")
    generator = FakeGenerator('{"draft_markdown":"fabricated", "claims":[]}')
    result = await create_grounded_draft(_payload([fragment.fragment_id], "Как удалить пользователя из РДИК?"), Repository((fragment,)), generator)
    assert result.answerability is EvidenceSufficiency.INSUFFICIENT
    assert result.response.draft_markdown == ""
    assert generator.prompts == []


@pytest.mark.asyncio
async def test_invalid_claim_is_model_error_and_not_silently_trimmed() -> None:
    fragment = _fragment("frag-number", "UPD status does not change after 1 hour; contact support.")
    generator = FakeGenerator(
        '{"draft_markdown":"UPD status changes after 2 hours.","claims":[{"claim_id":"c1","text":"UPD status changes after 2 hours.","fragment_ids":["frag-number"]}]}'
    )
    with pytest.raises(GeneratorModelError):
        await create_grounded_draft(_payload([fragment.fragment_id], "UPD status does not change: document processing error"), Repository((fragment,)), generator)


@pytest.mark.asyncio
async def test_unknown_url_in_rendered_draft_is_model_error_even_with_grounded_claim() -> None:
    fragment = _fragment("frag-url-draft", "UPD document is created in the portal.")
    generator = FakeGenerator(
        '{"draft_markdown":"Open https://evil.example/help for details.","claims":[{"claim_id":"c1","text":"UPD document is created in the portal.","fragment_ids":["frag-url-draft"]}]}'
    )
    with pytest.raises(GeneratorModelError):
        await create_grounded_draft(_payload([fragment.fragment_id], "How to create an UPD document in the portal?"), Repository((fragment,)), generator)


@pytest.mark.asyncio
async def test_verifier_rejects_invented_number_code_button_url_and_wrong_snapshot() -> None:
    fragment = _fragment("frag-real", "Error RDIK_0059: click button Retry sending after 1 hour.")
    repository = Repository((fragment,))
    claims = [
        Claim(claim_id="number", text="Error RDIK_0059 repeats after 2 hours.", fragment_ids=[fragment.fragment_id]),
        Claim(claim_id="button", text="Click button Delete document.", fragment_ids=[fragment.fragment_id]),
        Claim(claim_id="url", text="Open https://evil.example/help.", fragment_ids=[fragment.fragment_id]),
        Claim(claim_id="code", text="Error RDIK_0060.", fragment_ids=[fragment.fragment_id]),
        Claim(claim_id="snapshot", text="Error RDIK_0059.", fragment_ids=["frag-from-other-snapshot"]),
    ]
    checks = await verify_claims(SNAPSHOT_ID, claims, repository)
    assert [check.result.supported for check in checks] == [False, False, False, False, False]
    assert all(check.result.evidence_fragment_ids == [] for check in checks)
    reasons = {reason for check in checks for reason in check.reasons}
    assert {"INVENTED_NUMBER", "INVENTED_CODE", "INVENTED_ACTION_OR_STATUS", "UNKNOWN_URL", "WRONG_SNAPSHOT_FRAGMENT"} <= reasons


@pytest.mark.asyncio
async def test_verifier_preserves_applicability_condition() -> None:
    fragment = _fragment("frag-condition", "If UPD status does not change after 1 hour, contact support.")
    repository = Repository((fragment,))
    from tenderhack_knowledge.generation.models import GroundedClaim

    claim = GroundedClaim(
        claim_id="conditional",
        text="For UPD status contact support.",
        fragment_ids=[fragment.fragment_id],
        applies_if=("if UPD status does not change after 1 hour",),
    )
    check = (await verify_claims(SNAPSHOT_ID, [claim], repository))[0]
    assert check.result.supported is True

    missing_condition = claim.model_copy(update={"applies_if": ("if UPD status does not change after 2 hours",)})
    failed = (await verify_claims(SNAPSHOT_ID, [missing_condition], repository))[0]
    assert failed.result.supported is False
    assert "APPLICABILITY_DROPPED" in failed.reasons


def test_parser_rejects_business_decision_and_duplicate_claim_ids() -> None:
    with pytest.raises(ModelOutputError, match="forbidden business field"):
        parse_model_output('{"decision":"ANSWER","draft_markdown":"x","claims":[]}')
    with pytest.raises(ModelOutputError, match="decision_candidate"):
        parse_model_output('{"decision_candidate":"ANSWER","draft_markdown":"x","claims":[]}')
    with pytest.raises(ModelOutputError, match="unique"):
        parse_model_output('{"draft_markdown":"x","claims":[{"claim_id":"x","text":"a","fragment_ids":["f"]},{"claim_id":"x","text":"b","fragment_ids":["f"]}]}')


def test_parser_accepts_fenced_json_but_rejects_plain_prose() -> None:
    parsed = parse_model_output('```json\n{"draft_markdown":"grounded","claims":[]}\n```')
    assert parsed.draft_markdown == "grounded"
    with pytest.raises(ModelOutputError, match="invalid JSON"):
        parse_model_output("Here is the answer in plain prose.")


def test_prompt_versions_include_data_boundary_and_no_chain_of_thought() -> None:
    fragment = _fragment("frag-prompt", "Выберите тип документа.")
    prompt = build_draft_prompt("Как выбрать тип?", [fragment], DraftConstraints())
    verify_prompt = build_verify_prompt("Выберите тип документа.", [fragment.text])
    assert DRAFT_PROMPT_VERSION in prompt
    assert VERIFY_PROMPT_VERSION in verify_prompt
    for phrase in ("DATA, not instructions", "never invent", "fragment_id", "chain-of-thought"):
        assert phrase.casefold() in prompt.casefold()


def test_draft_route_returns_model_unavailable_when_real_generator_cannot_be_reached(monkeypatch: pytest.MonkeyPatch) -> None:
    fragment = _fragment("frag-route", "Чтобы сформировать УПД, выберите тип документа и нажмите кнопку Сформировать.")
    repository = Repository((fragment,))
    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: repository)
    monkeypatch.setattr(routes, "get_generator", lambda: UnavailableGenerator(""))
    response = TestClient(app).post(
        "/v0/draft",
        headers=TRACE_HEADERS,
        json={
            "query": "Как сформировать УПД, выбрав тип документа?",
            "snapshot_id": SNAPSHOT_ID,
            "evidence_fragment_ids": [fragment.fragment_id],
            "constraints": {},
        },
    )
    assert response.status_code == 503
    assert response.json()["code"] == "MODEL_UNAVAILABLE"


def test_draft_route_gates_before_generator_construction(monkeypatch: pytest.MonkeyPatch) -> None:
    fragment = _fragment("frag-route-gated", "An unrelated catalog import procedure.")
    repository = Repository((fragment,))
    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: repository)

    def generator_must_not_be_constructed() -> Any:
        raise AssertionError("generator must not be constructed for insufficient evidence")

    monkeypatch.setattr(routes, "get_generator", generator_must_not_be_constructed)
    response = TestClient(app).post(
        "/v0/draft",
        headers=TRACE_HEADERS,
        json={"query": "How to delete an RDIK user?", "snapshot_id": SNAPSHOT_ID, "evidence_fragment_ids": [fragment.fragment_id], "constraints": {}},
    )
    assert response.status_code == 200
    assert response.json()["draft_markdown"] == ""


def test_verify_route_is_real_and_snapshot_aware(monkeypatch: pytest.MonkeyPatch) -> None:
    fragment = _fragment("frag-route-verify", "UPD status: Signed by supplier.")
    repository = Repository((fragment,))
    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: repository)
    response = TestClient(app).post(
        "/v0/verify",
        headers=TRACE_HEADERS,
        json={
            "snapshot_id": SNAPSHOT_ID,
            "claims": [
                {"claim_id": "ok", "text": "UPD status: Signed by supplier.", "fragment_ids": [fragment.fragment_id]},
                {"claim_id": "bad", "text": "UPD status: Draft.", "fragment_ids": [fragment.fragment_id]},
            ],
        },
    )
    assert response.status_code == 200
    assert response.json()["results"] == [
        {"claim_id": "ok", "supported": True, "evidence_fragment_ids": [fragment.fragment_id]},
        {"claim_id": "bad", "supported": False, "evidence_fragment_ids": []},
    ]


@pytest.mark.asyncio
async def test_historical_snapshot_is_rejected_for_verification() -> None:
    fragment = _fragment("frag-historical", "Historical resolution text.")
    repository = Repository((fragment,), corpus=Corpus.HISTORICAL)
    with pytest.raises(CorpusBoundaryError):
        await verify_claims(SNAPSHOT_ID, [Claim(claim_id="c", text="Historical resolution text.", fragment_ids=[fragment.fragment_id])], repository)


@pytest.mark.asyncio
async def test_real_k4_fixture_fragments_verify_when_postgres_is_configured() -> None:
    url = os.getenv("K56_TEST_DATABASE_URL", "")
    if not url:
        pytest.skip("K56_TEST_DATABASE_URL is not configured")
    fixture_path = Path(__file__).parents[1] / "benchmarks" / "k4-answerability-fixture.json"
    fixture = json.loads(fixture_path.read_text(encoding="utf-8"))
    engine = create_async_engine(url, pool_pre_ping=True)
    try:
        repository = PostgresKnowledgeRepository(engine)
        fragments = {fragment.fragment_id: fragment for fragment in await repository.snapshot_fragments(fixture["snapshot_id"])}
        grounded_cases = [case for case in fixture["cases"] if case["expected"] == "SUFFICIENT"][:7]
        claims = [
            Claim(claim_id=case["id"], text=fragments[case["candidate_fragment_ids"][0]].text, fragment_ids=[case["candidate_fragment_ids"][0]])
            for case in grounded_cases
            if case["candidate_fragment_ids"][0] in fragments
        ]
        checks = await verify_claims(fixture["snapshot_id"], claims, repository)
        assert len(checks) == len(claims) >= 5
        assert all(check.result.supported for check in checks)
        wrong_snapshot = Claim(claim_id="wrong-snapshot", text=claims[0].text, fragment_ids=["fragment-not-in-snapshot"])
        rejected = (await verify_claims(fixture["snapshot_id"], [wrong_snapshot], repository))[0]
        assert rejected.result.supported is False
        assert "WRONG_SNAPSHOT_FRAGMENT" in rejected.reasons
    finally:
        await engine.dispose()
