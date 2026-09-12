from fastapi.testclient import TestClient

from tenderhack_knowledge.main import app


TRACE_HEADERS = {
    "X-Trace-Id": "00000000-0000-4000-8000-000000000000",
    "X-Case-Id": "case-1",
    "X-Turn-Id": "turn-1",
}
TRACE_ONLY = {"X-Trace-Id": TRACE_HEADERS["X-Trace-Id"]}


def test_understand_accepts_typed_body_and_returns_typed_language_flags() -> None:
    response = TestClient(app).post(
        "/v0/understand",
        headers=TRACE_HEADERS,
        json={"text": "How do I submit a tender?", "prior_turn_summary": None},
    )

    assert response.status_code == 200
    assert response.json()["language_flags"] == {"detected_language": "unknown", "typo_corrected": False}


def test_missing_body_field_uses_frozen_validation_error_shape() -> None:
    response = TestClient(app).post("/v0/understand", headers=TRACE_HEADERS, json={})

    assert response.status_code == 422
    assert response.json()["code"] == "VALIDATION_ERROR"
    assert response.json()["detail"]["errors"][0]["loc"] == ["body", "text"]


def test_missing_runtime_header_uses_frozen_missing_header_error() -> None:
    response = TestClient(app).post("/v0/understand", headers=TRACE_ONLY, json={"text": "x"})

    assert response.status_code == 400
    assert response.json()["code"] == "MISSING_TRACE_HEADER"
    assert "X-Case-Id" in response.json()["message"]
    assert "X-Turn-Id" in response.json()["message"]


def test_trace_header_requires_uuid_v4() -> None:
    response = TestClient(app).post(
        "/v0/understand",
        headers={**TRACE_HEADERS, "X-Trace-Id": "not-a-uuid"},
        json={"text": "x"},
    )

    assert response.status_code == 422
    assert response.json()["code"] == "VALIDATION_ERROR"


def test_retrieve_validates_entities_exact_codes_and_corpus() -> None:
    valid_entity = {"type": "document_type", "value": "invoice", "provenance": "user_explicit"}
    valid = TestClient(app).post(
        "/v0/retrieve",
        headers=TRACE_HEADERS,
        json={"query": "invoice", "entities": [valid_entity], "exact_codes": ["INV-1"], "corpus": "NORMATIVE"},
    )
    invalid = TestClient(app).post(
        "/v0/retrieve",
        headers=TRACE_HEADERS,
        json={"query": "invoice", "entities": [], "exact_codes": "INV-1", "corpus": "UNKNOWN"},
    )

    assert valid.status_code == 200
    assert valid.json()["snapshot_id"] == "snapshot-stub-v0"
    assert invalid.status_code == 422
    assert invalid.json()["code"] == "VALIDATION_ERROR"


def test_answerability_and_draft_have_typed_boundaries() -> None:
    client = TestClient(app)
    answerability = client.post(
        "/v0/answerability",
        headers=TRACE_HEADERS,
        json={"query": "invoice", "snapshot_id": "snapshot-stub-v0", "candidate_fragment_ids": []},
    )
    draft = client.post(
        "/v0/draft",
        headers=TRACE_HEADERS,
        json={
            "query": "invoice",
            "snapshot_id": "snapshot-stub-v0",
            "evidence_fragment_ids": [],
            "constraints": {"max_output_tokens": 200, "tone": None},
        },
    )

    assert answerability.status_code == 200
    assert answerability.json()["evidence_sufficiency"] == "INSUFFICIENT"
    assert draft.status_code == 200
    assert draft.json()["token_usage"] == {"input": 0, "output": 0}


def test_verify_accepts_typed_claims() -> None:
    response = TestClient(app).post(
        "/v0/verify",
        headers=TRACE_HEADERS,
        json={
            "snapshot_id": "snapshot-stub-v0",
            "claims": [{"claim_id": "claim-1", "text": "A", "fragment_ids": ["fragment-1"]}],
        },
    )

    assert response.status_code == 200
    assert response.json()["results"] == [{"claim_id": "claim-1", "supported": False, "evidence_fragment_ids": []}]


def test_quality_push_endpoints_validate_frozen_payloads() -> None:
    client = TestClient(app)
    turn_payload = {
        "case_id": "case-1",
        "turn_id": "turn-1",
        "revision": 1,
        "occurred_at": "2026-09-12T12:00:00Z",
        "question_text": "question",
        "decision": "ANSWER",
        "reason_codes": ["EVIDENCE_SUFFICIENT"],
        "stage_timings": {"understand_ms": 10},
    }
    feedback_payload = {
        "feedback_id": "feedback-1",
        "case_id": "case-1",
        "turn_id": "turn-1",
        "occurred_at": "2026-09-12T12:00:00Z",
        "specialist_rating": "POSITIVE",
        "information_quality_rating": "NEGATIVE",
        "integration_mode": "SIMULATED",
        "solved": False,
    }
    completion_payload = {
        "case_id": "case-1",
        "completed_at": "2026-09-12T12:00:00Z",
        "completion_reason": "USER_CLOSED",
        "resolution_status": "UNKNOWN",
        "integration_mode": "SIMULATED",
    }

    responses = [
        client.post("/v0/quality/turns", headers=TRACE_ONLY, json=turn_payload),
        client.post("/v0/quality/feedback", headers=TRACE_ONLY, json=feedback_payload),
        client.post("/v0/quality/completions", headers=TRACE_ONLY, json=completion_payload),
    ]
    invalid_turn = client.post(
        "/v0/quality/turns",
        headers=TRACE_ONLY,
        json={**turn_payload, "stage_timings": "not-an-object"},
    )

    assert [response.status_code for response in responses] == [202, 202, 202]
    assert all(response.json() == {"status": "stored"} for response in responses)
    assert invalid_turn.status_code == 422
    assert invalid_turn.json()["code"] == "VALIDATION_ERROR"


def test_knowledge_responses_do_not_expose_support_decisions() -> None:
    forbidden = {"Decision", "decision", "should_handoff", "HandoffStatus", "handoff_status", "ResolutionStatus", "resolution_status"}
    client = TestClient(app)
    response_bodies = [
        client.get("/health").json(),
        client.post("/v0/understand", headers=TRACE_HEADERS, json={"text": "x"}).json(),
        client.post(
            "/v0/moderation/context",
            headers=TRACE_HEADERS,
            json={"text": "x", "rule_match": {"rule_id": "r", "version": "1", "matched_term": "x", "matched_span": {"start": 0, "end": 1}}},
        ).json(),
        client.post(
            "/v0/retrieve",
            headers=TRACE_HEADERS,
            json={"query": "x", "entities": [], "exact_codes": [], "corpus": "NORMATIVE"},
        ).json(),
        client.post(
            "/v0/answerability",
            headers=TRACE_HEADERS,
            json={"query": "x", "snapshot_id": "s", "candidate_fragment_ids": []},
        ).json(),
        client.post(
            "/v0/draft",
            headers=TRACE_HEADERS,
            json={"query": "x", "snapshot_id": "s", "evidence_fragment_ids": [], "constraints": {}},
        ).json(),
        client.post("/v0/verify", headers=TRACE_HEADERS, json={"claims": [], "snapshot_id": "s"}).json(),
    ]

    assert all(not forbidden.intersection(body) for body in response_bodies)
