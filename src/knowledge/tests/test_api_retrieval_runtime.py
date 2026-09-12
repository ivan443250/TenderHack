from tenderhack_knowledge.api import routes
from tenderhack_knowledge.contracts.v0 import Candidate, CandidateScores
from tenderhack_knowledge.main import app
from tenderhack_knowledge.retrieval.service import RetrievalResult
from fastapi.testclient import TestClient


TRACE_HEADERS = {
    "X-Trace-Id": "00000000-0000-4000-8000-000000000000",
    "X-Case-Id": "case-runtime",
    "X-Turn-Id": "turn-runtime",
}


def test_retrieve_uses_lexical_primary_without_constructing_inference(monkeypatch) -> None:
    calls: dict[str, object] = {}

    class FakeLexicalRetriever:
        config_version = "lexical-v1"

        def __init__(self, repository: object) -> None:
            calls["repository"] = repository

        async def retrieve(self, payload, *, mode: str, allow_trigram: bool, limit: int):
            calls["payload"] = payload
            calls["mode"] = mode
            calls["allow_trigram"] = allow_trigram
            calls["limit"] = limit
            candidate = Candidate(
                fragment_id="frag-runtime",
                document_id="doc-runtime",
                page=1,
                anchor=None,
                scores=CandidateScores(exact=1.0),
                applicability_flags=[],
            )
            return RetrievalResult(
                snapshot_id="snap-runtime",
                candidates=(candidate,),
                records=(),
            )

    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: object())
    monkeypatch.setattr(routes, "LexicalRetriever", FakeLexicalRetriever)

    response = TestClient(app).post(
        "/v0/retrieve",
        headers=TRACE_HEADERS,
        json={
            "query": "Как подписать УПД?",
            "entities": [],
            "exact_codes": [],
            "corpus": "NORMATIVE",
        },
    )

    assert response.status_code == 200, response.text
    assert response.json()["snapshot_id"] == "snap-runtime"
    assert response.json()["retrieval_config_version"] == "lexical-v1"
    assert response.json()["candidates"][0]["fragment_id"] == "frag-runtime"
    assert calls["mode"] == "hybrid"
    assert calls["allow_trigram"] is True
    assert calls["limit"] == 10
    assert calls["payload"].exact_codes == ["УПД"]


def test_retrieve_preserves_explicit_and_understood_exact_codes(monkeypatch) -> None:
    observed: dict[str, object] = {}

    class FakeLexicalRetriever:
        config_version = "lexical-v1"

        def __init__(self, _repository: object) -> None:
            pass

        async def retrieve(self, payload, **_kwargs):
            observed["exact_codes"] = payload.exact_codes
            return RetrievalResult(snapshot_id="snap-runtime", candidates=(), records=())

    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: object())
    monkeypatch.setattr(routes, "LexicalRetriever", FakeLexicalRetriever)

    response = TestClient(app).post(
        "/v0/retrieve",
        headers=TRACE_HEADERS,
        json={
            "query": "найти УПД",
            "entities": [],
            "exact_codes": ["CUSTOM-42", "УПД"],
            "corpus": "NORMATIVE",
        },
    )

    assert response.status_code == 200, response.text
    assert observed["exact_codes"] == ["CUSTOM-42", "УПД"]
