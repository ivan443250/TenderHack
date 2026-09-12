from typing import Any

from fastapi import APIRouter, Header, HTTPException, Query

from tenderhack_knowledge.contracts.v0 import (
    AcceptedResponse,
    AnswerabilityResponse,
    Candidate,
    DraftResponse,
    HealthResponse,
    IssueGroupsResponse,
    ModerationContextResponse,
    QualityEvaluationsResponse,
    RetrieveResponse,
    SnapshotResponse,
    SourceResponse,
    TokenUsage,
    UnderstandResponse,
    VerifyResult,
    VerifyResponse,
    now_iso,
)

router = APIRouter()


def require_trace(x_trace_id: str | None) -> str:
    if not x_trace_id:
        raise HTTPException(status_code=400, detail={"code": "MISSING_TRACE_HEADER", "message": "X-Trace-Id is required"})
    return x_trace_id


def require_runtime_headers(
    x_trace_id: str | None,
    x_case_id: str | None,
    x_turn_id: str | None,
) -> None:
    """Enforce the frozen tracing headers for orchestration-stage calls."""
    missing = [
        name
        for name, value in (("X-Trace-Id", x_trace_id), ("X-Case-Id", x_case_id), ("X-Turn-Id", x_turn_id))
        if not value
    ]
    if missing:
        raise HTTPException(
            status_code=400,
            detail={"code": "MISSING_TRACE_HEADER", "message": f"Required headers missing: {', '.join(missing)}"},
        )


def require_fields(payload: dict[str, Any], fields: tuple[str, ...]) -> None:
    missing = [field for field in fields if field not in payload]
    if missing:
        raise HTTPException(status_code=422, detail={"code": "VALIDATION_ERROR", "message": f"Missing fields: {', '.join(missing)}"})


@router.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(status="ok", model_versions=None)


@router.post("/v0/understand", response_model=UnderstandResponse)
def understand(
    payload: dict[str, Any],
    x_trace_id: str | None = Header(default=None),
    x_case_id: str | None = Header(default=None),
    x_turn_id: str | None = Header(default=None),
) -> UnderstandResponse:
    require_runtime_headers(x_trace_id, x_case_id, x_turn_id)
    require_fields(payload, ("text",))
    text = str(payload.get("text", ""))
    return UnderstandResponse(normalized_text=text, entities=[], exact_codes=[], language_flags={"detected_language": "unknown", "typo_corrected": False})


@router.post("/v0/moderation/context", response_model=ModerationContextResponse)
def moderation_context(
    payload: dict[str, Any],
    x_trace_id: str | None = Header(default=None),
    x_case_id: str | None = Header(default=None),
    x_turn_id: str | None = Header(default=None),
) -> ModerationContextResponse:
    require_runtime_headers(x_trace_id, x_case_id, x_turn_id)
    require_fields(payload, ("text", "rule_match"))
    return ModerationContextResponse(ambiguity="UNCERTAIN", model_version="stub-v0")


@router.post("/v0/retrieve", response_model=RetrieveResponse)
def retrieve(
    payload: dict[str, Any],
    x_trace_id: str | None = Header(default=None),
    x_case_id: str | None = Header(default=None),
    x_turn_id: str | None = Header(default=None),
) -> RetrieveResponse:
    require_runtime_headers(x_trace_id, x_case_id, x_turn_id)
    require_fields(payload, ("query", "entities", "exact_codes", "corpus"))
    return RetrieveResponse(snapshot_id=str(payload.get("snapshot_id") or "snapshot-stub-v0"), retrieval_config_version="stub-v0", candidates=[])


@router.post("/v0/answerability", response_model=AnswerabilityResponse)
def answerability(
    payload: dict[str, Any],
    x_trace_id: str | None = Header(default=None),
    x_case_id: str | None = Header(default=None),
    x_turn_id: str | None = Header(default=None),
) -> AnswerabilityResponse:
    require_runtime_headers(x_trace_id, x_case_id, x_turn_id)
    require_fields(payload, ("query", "snapshot_id", "candidate_fragment_ids"))
    return AnswerabilityResponse(evidence_sufficiency="INSUFFICIENT", evidence_fragment_ids=[], missing_conditions=[], risk_flags=["stub"])


@router.post("/v0/draft", response_model=DraftResponse)
def draft(
    payload: dict[str, Any],
    x_trace_id: str | None = Header(default=None),
    x_case_id: str | None = Header(default=None),
    x_turn_id: str | None = Header(default=None),
) -> DraftResponse:
    require_runtime_headers(x_trace_id, x_case_id, x_turn_id)
    require_fields(payload, ("query", "snapshot_id", "evidence_fragment_ids", "constraints"))
    return DraftResponse(draft_markdown="", claims=[], model_version="stub-v0", token_usage=TokenUsage(input=0, output=0))


@router.post("/v0/verify", response_model=VerifyResponse)
def verify(
    payload: dict[str, Any],
    x_trace_id: str | None = Header(default=None),
    x_case_id: str | None = Header(default=None),
    x_turn_id: str | None = Header(default=None),
) -> VerifyResponse:
    require_runtime_headers(x_trace_id, x_case_id, x_turn_id)
    require_fields(payload, ("claims", "snapshot_id"))
    return VerifyResponse(
        results=[VerifyResult(claim_id=claim.get("claim_id", "unknown"), supported=False, evidence_fragment_ids=[]) for claim in payload.get("claims", [])]
    )


@router.get("/v0/sources/{fragment_id}", response_model=SourceResponse)
def source(fragment_id: str, x_trace_id: str | None = Header(default=None)) -> SourceResponse:
    require_trace(x_trace_id)
    return SourceResponse(document_id="stub-document", title="Stub source", version="stub-v0", text="", snapshot_id="snapshot-stub-v0")


@router.get("/v0/snapshots/current", response_model=SnapshotResponse)
def current_snapshot(x_trace_id: str | None = Header(default=None)) -> SnapshotResponse:
    require_trace(x_trace_id)
    return SnapshotResponse(snapshot_id="snapshot-stub-v0", created_at=now_iso(), document_count=0, fragment_count=0)


@router.post("/v0/quality/turns", response_model=AcceptedResponse, status_code=202)
def quality_turn(payload: dict[str, Any], x_trace_id: str | None = Header(default=None)) -> AcceptedResponse:
    require_trace(x_trace_id)
    require_fields(payload, ("case_id", "turn_id", "revision", "occurred_at", "question_text", "decision", "reason_codes", "stage_timings"))
    return AcceptedResponse(status="stored")


@router.post("/v0/quality/feedback", response_model=AcceptedResponse, status_code=202)
def quality_feedback(payload: dict[str, Any], x_trace_id: str | None = Header(default=None)) -> AcceptedResponse:
    require_trace(x_trace_id)
    require_fields(payload, ("feedback_id", "case_id", "turn_id", "occurred_at"))
    return AcceptedResponse(status="stored")


@router.post("/v0/quality/completions", response_model=AcceptedResponse, status_code=202)
def quality_completion(payload: dict[str, Any], x_trace_id: str | None = Header(default=None)) -> AcceptedResponse:
    require_trace(x_trace_id)
    require_fields(payload, ("case_id", "completed_at", "completion_reason", "resolution_status"))
    return AcceptedResponse(status="stored")


@router.get("/v0/quality/evaluations", response_model=QualityEvaluationsResponse)
def quality_evaluations(case_id: str = Query(...), x_trace_id: str | None = Header(default=None)) -> QualityEvaluationsResponse:
    require_trace(x_trace_id)
    return QualityEvaluationsResponse(case_id=case_id, evaluations=[])


@router.get("/v0/quality/issue-groups", response_model=IssueGroupsResponse)
def issue_groups(x_trace_id: str | None = Header(default=None)) -> IssueGroupsResponse:
    require_trace(x_trace_id)
    return IssueGroupsResponse(groups=[])
