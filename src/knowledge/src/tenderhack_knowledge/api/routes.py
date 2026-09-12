from functools import lru_cache
from typing import Annotated
from uuid import UUID

from fastapi import APIRouter, Header, HTTPException, Query
from pydantic import AfterValidator

from tenderhack_knowledge.contracts.v0 import (
    AcceptedResponse,
    AnswerabilityRequest,
    AnswerabilityResponse,
    DraftRequest,
    DraftResponse,
    ErrorResponse,
    HealthResponse,
    IssueGroupsResponse,
    ModerationContextRequest,
    ModerationContextResponse,
    QualityCompletionPush,
    QualityEvaluationsResponse,
    QualityFeedbackPush,
    QualityTurnPush,
    RetrieveRequest,
    RetrieveResponse,
    SnapshotResponse,
    SourceResponse,
    TokenUsage,
    UnderstandRequest,
    UnderstandResponse,
    VerifyRequest,
    VerifyResponse,
    VerifyResult,
)
from tenderhack_knowledge.ingestion.ids import normalize_source_name
from tenderhack_knowledge.ingestion.repository import UnknownFragmentError, UnknownSnapshotError
from tenderhack_knowledge.persistence.db import create_engine
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository

router = APIRouter()


@lru_cache(maxsize=1)
def get_knowledge_repository() -> PostgresKnowledgeRepository | None:
    """Build the durable repository lazily; importing the API never opens a DB."""

    engine = create_engine()
    return PostgresKnowledgeRepository(engine) if engine is not None else None


def _internal_error(message: str) -> HTTPException:
    from tenderhack_knowledge.contracts.v0 import ErrorCode

    return HTTPException(status_code=500, detail={"code": ErrorCode.INTERNAL_ERROR.value, "message": message})


def _safe_title(filename: str) -> str:
    normalized = normalize_source_name(filename)
    return normalized.rsplit("/", 1)[-1] or "source.pdf"


def _anchor_text(resolution: object) -> str | None:
    anchor = resolution.source_anchor
    if anchor.bbox is not None:
        return "bbox:" + ",".join(str(value) for value in anchor.bbox)
    return anchor.section or resolution.section


def _require_uuid4(value: UUID) -> UUID:
    if value.version != 4:
        raise ValueError("X-Trace-Id must be a UUID v4")
    return value


TraceIdHeader = Annotated[UUID, Header(alias="X-Trace-Id"), AfterValidator(_require_uuid4)]
CaseIdHeader = Annotated[str, Header(alias="X-Case-Id")]
TurnIdHeader = Annotated[str, Header(alias="X-Turn-Id")]

ORCHESTRATION_ERRORS = {
    400: {"model": ErrorResponse},
    422: {"model": ErrorResponse},
    500: {"model": ErrorResponse},
    503: {"model": ErrorResponse},
}
RETRIEVAL_ERRORS = {
    **ORCHESTRATION_ERRORS,
    404: {"model": ErrorResponse},
}
QUALITY_ERRORS = {
    400: {"model": ErrorResponse},
    422: {"model": ErrorResponse},
    500: {"model": ErrorResponse},
}


@router.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(status="ok", model_versions=None)


@router.post(
    "/v0/understand",
    response_model=UnderstandResponse,
    responses=ORCHESTRATION_ERRORS,
)
def understand(
    payload: UnderstandRequest,
    x_trace_id: TraceIdHeader,
    x_case_id: CaseIdHeader,
    x_turn_id: TurnIdHeader,
) -> UnderstandResponse:
    del x_trace_id, x_case_id, x_turn_id
    return UnderstandResponse(
        normalized_text=payload.text,
        entities=[],
        exact_codes=[],
        language_flags={"detected_language": "unknown", "typo_corrected": False},
    )


@router.post(
    "/v0/moderation/context",
    response_model=ModerationContextResponse,
    responses=ORCHESTRATION_ERRORS,
)
def moderation_context(
    payload: ModerationContextRequest,
    x_trace_id: TraceIdHeader,
    x_case_id: CaseIdHeader,
    x_turn_id: TurnIdHeader,
) -> ModerationContextResponse:
    del payload, x_trace_id, x_case_id, x_turn_id
    return ModerationContextResponse(ambiguity="UNCERTAIN", model_version="stub-v0")


@router.post(
    "/v0/retrieve",
    response_model=RetrieveResponse,
    responses=RETRIEVAL_ERRORS,
)
def retrieve(
    payload: RetrieveRequest,
    x_trace_id: TraceIdHeader,
    x_case_id: CaseIdHeader,
    x_turn_id: TurnIdHeader,
) -> RetrieveResponse:
    del x_trace_id, x_case_id, x_turn_id
    snapshot_id = payload.snapshot_id or "snapshot-stub-v0"
    return RetrieveResponse(snapshot_id=snapshot_id, retrieval_config_version="stub-v0", candidates=[])


@router.post(
    "/v0/answerability",
    response_model=AnswerabilityResponse,
    responses=RETRIEVAL_ERRORS,
)
def answerability(
    payload: AnswerabilityRequest,
    x_trace_id: TraceIdHeader,
    x_case_id: CaseIdHeader,
    x_turn_id: TurnIdHeader,
) -> AnswerabilityResponse:
    del payload, x_trace_id, x_case_id, x_turn_id
    return AnswerabilityResponse(evidence_sufficiency="INSUFFICIENT", evidence_fragment_ids=[], missing_conditions=[], risk_flags=["stub"])


@router.post(
    "/v0/draft",
    response_model=DraftResponse,
    responses=RETRIEVAL_ERRORS,
)
def draft(
    payload: DraftRequest,
    x_trace_id: TraceIdHeader,
    x_case_id: CaseIdHeader,
    x_turn_id: TurnIdHeader,
) -> DraftResponse:
    del payload, x_trace_id, x_case_id, x_turn_id
    return DraftResponse(draft_markdown="", claims=[], model_version="stub-v0", token_usage=TokenUsage(input=0, output=0))


@router.post(
    "/v0/verify",
    response_model=VerifyResponse,
    responses=RETRIEVAL_ERRORS,
)
def verify(
    payload: VerifyRequest,
    x_trace_id: TraceIdHeader,
    x_case_id: CaseIdHeader,
    x_turn_id: TurnIdHeader,
) -> VerifyResponse:
    del x_trace_id, x_case_id, x_turn_id
    return VerifyResponse(
        results=[VerifyResult(claim_id=claim.claim_id, supported=False, evidence_fragment_ids=[]) for claim in payload.claims]
    )


@router.get(
    "/v0/sources/{fragment_id}",
    response_model=SourceResponse,
    responses={400: {"model": ErrorResponse}, 404: {"model": ErrorResponse}, 422: {"model": ErrorResponse}, 500: {"model": ErrorResponse}},
)
async def source(fragment_id: str, x_trace_id: TraceIdHeader) -> SourceResponse:
    del x_trace_id
    repository = get_knowledge_repository()
    if repository is None:
        raise _internal_error("knowledge database is not configured")
    try:
        resolution = await repository.resolve_source(fragment_id)
    except UnknownFragmentError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(
            status_code=404,
            detail={"code": ErrorCode.UNKNOWN_FRAGMENT.value, "message": f"Unknown fragment: {fragment_id}"},
        ) from exc
    except UnknownSnapshotError as exc:
        raise _internal_error("no current normative snapshot") from exc
    except Exception as exc:  # pragma: no cover - depends on external DB
        raise _internal_error("unable to resolve source fragment") from exc
    return SourceResponse(
        document_id=resolution.document_id,
        title=_safe_title(resolution.original_filename),
        version=resolution.declared_version or resolution.document_version_id,
        page=resolution.page_start,
        anchor=_anchor_text(resolution),
        text=resolution.text,
        snapshot_id=resolution.snapshot_id or "",
    )


@router.get(
    "/v0/snapshots/current",
    response_model=SnapshotResponse,
    responses={400: {"model": ErrorResponse}, 422: {"model": ErrorResponse}, 500: {"model": ErrorResponse}},
)
async def current_snapshot(x_trace_id: TraceIdHeader) -> SnapshotResponse:
    del x_trace_id
    repository = get_knowledge_repository()
    if repository is None:
        raise _internal_error("knowledge database is not configured")
    try:
        snapshot = await repository.get_current_normative_snapshot()
        if snapshot is None:
            raise _internal_error("no current normative snapshot")
        document_count, fragment_count = await repository.snapshot_counts(snapshot.snapshot_id)
    except HTTPException:
        raise
    except Exception as exc:  # pragma: no cover - depends on external DB
        raise _internal_error("unable to load current normative snapshot") from exc
    return SnapshotResponse(
        snapshot_id=snapshot.snapshot_id,
        created_at=snapshot.created_at,
        document_count=document_count,
        fragment_count=fragment_count,
    )


@router.post(
    "/v0/quality/turns",
    response_model=AcceptedResponse,
    status_code=202,
    responses=QUALITY_ERRORS,
)
def quality_turn(payload: QualityTurnPush, x_trace_id: TraceIdHeader) -> AcceptedResponse:
    del payload, x_trace_id
    return AcceptedResponse(status="stored")


@router.post(
    "/v0/quality/feedback",
    response_model=AcceptedResponse,
    status_code=202,
    responses=QUALITY_ERRORS,
)
def quality_feedback(payload: QualityFeedbackPush, x_trace_id: TraceIdHeader) -> AcceptedResponse:
    del payload, x_trace_id
    return AcceptedResponse(status="stored")


@router.post(
    "/v0/quality/completions",
    response_model=AcceptedResponse,
    status_code=202,
    responses=QUALITY_ERRORS,
)
def quality_completion(payload: QualityCompletionPush, x_trace_id: TraceIdHeader) -> AcceptedResponse:
    del payload, x_trace_id
    return AcceptedResponse(status="stored")


@router.get(
    "/v0/quality/evaluations",
    response_model=QualityEvaluationsResponse,
    responses={400: {"model": ErrorResponse}, 422: {"model": ErrorResponse}, 500: {"model": ErrorResponse}},
)
def quality_evaluations(x_trace_id: TraceIdHeader, case_id: str = Query(...)) -> QualityEvaluationsResponse:
    del x_trace_id
    return QualityEvaluationsResponse(case_id=case_id, evaluations=[])


@router.get(
    "/v0/quality/issue-groups",
    response_model=IssueGroupsResponse,
    responses={400: {"model": ErrorResponse}, 422: {"model": ErrorResponse}, 500: {"model": ErrorResponse}},
)
def issue_groups(x_trace_id: TraceIdHeader) -> IssueGroupsResponse:
    del x_trace_id
    return IssueGroupsResponse(groups=[])
