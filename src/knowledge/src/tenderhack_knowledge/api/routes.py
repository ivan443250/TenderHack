import logging
import sys
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
    MaterialSection,
    MaterialSectionsResponse,
    MaterialsResponse,
    MaterialSummary,
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
from tenderhack_knowledge.answerability import assess_answerability
from tenderhack_knowledge.generation import create_grounded_draft
from tenderhack_knowledge.generation.service import GeneratorModelError, GeneratorUnavailableError
from tenderhack_knowledge.inference.config import InferenceSettings
from tenderhack_knowledge.inference.generator import LocalOpenAIChatGenerator
from tenderhack_knowledge.ingestion.ids import normalize_source_name
from tenderhack_knowledge.ingestion.repository import (
    CorpusBoundaryError,
    UnknownDocumentError,
    UnknownFragmentError,
    UnknownSnapshotError,
)
from tenderhack_knowledge.persistence.db import create_engine
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository
from tenderhack_knowledge.quality.service import QualityPayloadConflict, QualityRepository
from tenderhack_knowledge.retrieval import LexicalRetriever
from tenderhack_knowledge.understanding import understand_query

router = APIRouter()
logger = logging.getLogger(__name__)


@lru_cache(maxsize=1)
def get_knowledge_repository() -> PostgresKnowledgeRepository | None:
    """Build the durable repository lazily; importing the API never opens a DB.

    Cached as a process-wide singleton — one engine/connection pool for the
    life of the process, not one per request. `main.py`'s lifespan disposes
    the underlying engine on shutdown.
    """

    engine = create_engine()
    return PostgresKnowledgeRepository(engine) if engine is not None else None


@lru_cache(maxsize=1)
def get_quality_repository() -> QualityRepository | None:
    """Reuse the same engine/pool as `get_knowledge_repository` — no second connection pool."""

    knowledge_repository = get_knowledge_repository()
    return QualityRepository(knowledge_repository.engine) if knowledge_repository is not None else None


def get_generator() -> LocalOpenAIChatGenerator:
    """Create the lazy local generator client without loading model weights."""

    settings = InferenceSettings.from_env()
    return LocalOpenAIChatGenerator(
        base_url=settings.generator_base_url,
        model_id=settings.generator_model_id,
        revision=settings.generator_revision,
        timeout_seconds=settings.generator_timeout_seconds,
        temperature=settings.generator_temperature,
        top_p=settings.generator_top_p,
        top_k=settings.generator_top_k,
        max_tokens=settings.generator_max_tokens,
    )


def _internal_error(message: str) -> HTTPException:
    from tenderhack_knowledge.contracts.v0 import ErrorCode

    # Log with the original traceback (when raised via `raise _internal_error(...) from exc` inside an
    # except block) so a 500 is diagnosable from server logs instead of only the opaque client message.
    if sys.exc_info()[0] is not None:
        logger.exception(message)
    else:
        logger.error(message)

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
    return understand_query(payload.text).to_contract()


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
async def retrieve(
    payload: RetrieveRequest,
    x_trace_id: TraceIdHeader,
    x_case_id: CaseIdHeader,
    x_turn_id: TurnIdHeader,
) -> RetrieveResponse:
    del x_trace_id, x_case_id, x_turn_id
    repository = get_knowledge_repository()
    if repository is None:
        snapshot_id = payload.snapshot_id or "snapshot-stub-v0"
        return RetrieveResponse(snapshot_id=snapshot_id, retrieval_config_version="stub-v0", candidates=[])

    understanding = understand_query(payload.query)
    exact_codes = list(dict.fromkeys([*payload.exact_codes, *understanding.exact_codes]))
    request = payload.model_copy(update={"exact_codes": exact_codes})
    try:
        retriever = LexicalRetriever(repository)
        result = await retriever.retrieve(
            request,
            mode="hybrid",
            allow_trigram=True,
            limit=10,
        )
    except UnknownSnapshotError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(
            status_code=404,
            detail={"code": ErrorCode.UNKNOWN_SNAPSHOT.value, "message": f"Unknown snapshot: {payload.snapshot_id or exc}"},
        ) from exc
    except CorpusBoundaryError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(
            status_code=422,
            detail={"code": ErrorCode.VALIDATION_ERROR.value, "message": str(exc)},
        ) from exc
    except Exception as exc:  # pragma: no cover - depends on external DB/runtime
        raise _internal_error("unable to retrieve lexical candidates") from exc
    return RetrieveResponse(
        snapshot_id=result.snapshot_id,
        retrieval_config_version=retriever.config_version,
        candidates=list(result.candidates),
    )


@router.post(
    "/v0/answerability",
    response_model=AnswerabilityResponse,
    responses=RETRIEVAL_ERRORS,
)
async def answerability(
    payload: AnswerabilityRequest,
    x_trace_id: TraceIdHeader,
    x_case_id: CaseIdHeader,
    x_turn_id: TurnIdHeader,
) -> AnswerabilityResponse:
    del x_trace_id, x_case_id, x_turn_id
    repository = get_knowledge_repository()
    if repository is None:
        # A missing knowledge database is infrastructure failure evidence, not
        # proof that the normative corpus lacks an answer.
        return AnswerabilityResponse(
            evidence_sufficiency="INSUFFICIENT",
            evidence_fragment_ids=[],
            missing_conditions=[],
            risk_flags=["KNOWLEDGE_UNAVAILABLE"],
        )
    try:
        assessment = await assess_answerability(
            payload.query,
            payload.snapshot_id,
            payload.candidate_fragment_ids,
            repository,
        )
    except UnknownSnapshotError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(
            status_code=404,
            detail={"code": ErrorCode.UNKNOWN_SNAPSHOT.value, "message": f"Unknown snapshot: {payload.snapshot_id}"},
        ) from exc
    except CorpusBoundaryError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(
            status_code=422,
            detail={"code": ErrorCode.VALIDATION_ERROR.value, "message": str(exc)},
        ) from exc
    except Exception as exc:  # pragma: no cover - depends on external DB
        raise _internal_error("unable to assess evidence answerability") from exc
    return assessment.to_contract()


@router.post(
    "/v0/draft",
    response_model=DraftResponse,
    responses=RETRIEVAL_ERRORS,
)
async def draft(
    payload: DraftRequest,
    x_trace_id: TraceIdHeader,
    x_case_id: CaseIdHeader,
    x_turn_id: TurnIdHeader,
) -> DraftResponse:
    del x_trace_id, x_case_id, x_turn_id
    repository = get_knowledge_repository()
    if repository is None:
        # No evidence repository means the answerability gate cannot pass.
        # Return an empty, explicitly gated response rather than fabricating a
        # draft or pretending that a model was available.
        return DraftResponse(
            draft_markdown="",
            claims=[],
            model_version="draft_v1:gated:INSUFFICIENT",
            token_usage=TokenUsage(input=0, output=0),
        )
    try:
        result = await create_grounded_draft(payload, repository, generator_factory=get_generator)
    except UnknownSnapshotError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(
            status_code=404,
            detail={"code": ErrorCode.UNKNOWN_SNAPSHOT.value, "message": f"Unknown snapshot: {payload.snapshot_id}"},
        ) from exc
    except CorpusBoundaryError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(status_code=422, detail={"code": ErrorCode.VALIDATION_ERROR.value, "message": str(exc)}) from exc
    except GeneratorUnavailableError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(
            status_code=503,
            detail={"code": ErrorCode.MODEL_UNAVAILABLE.value, "message": "local generator is unavailable"},
        ) from exc
    except GeneratorModelError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(
            status_code=500,
            detail={"code": ErrorCode.MODEL_ERROR.value, "message": str(exc)},
        ) from exc
    except Exception as exc:  # pragma: no cover - depends on external DB/runtime
        raise _internal_error("unable to generate grounded draft") from exc
    return result.response


@router.post(
    "/v0/verify",
    response_model=VerifyResponse,
    responses=RETRIEVAL_ERRORS,
)
async def verify(
    payload: VerifyRequest,
    x_trace_id: TraceIdHeader,
    x_case_id: CaseIdHeader,
    x_turn_id: TurnIdHeader,
) -> VerifyResponse:
    del x_trace_id, x_case_id, x_turn_id
    repository = get_knowledge_repository()
    if repository is None:
        # Without a source repository no claim can be supported. Returning
        # explicit negatives preserves the frozen response shape and avoids a
        # misleading model/runtime error for an empty local development setup.
        return VerifyResponse(
            results=[VerifyResult(claim_id=claim.claim_id, supported=False, evidence_fragment_ids=[]) for claim in payload.claims]
        )
    from tenderhack_knowledge.verification import verify_claims

    try:
        checks = await verify_claims(payload.snapshot_id, payload.claims, repository)
    except UnknownSnapshotError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(
            status_code=404,
            detail={"code": ErrorCode.UNKNOWN_SNAPSHOT.value, "message": f"Unknown snapshot: {payload.snapshot_id}"},
        ) from exc
    except CorpusBoundaryError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(status_code=422, detail={"code": ErrorCode.VALIDATION_ERROR.value, "message": str(exc)}) from exc
    except Exception as exc:  # pragma: no cover - depends on external DB
        raise _internal_error("unable to verify grounded claims") from exc
    return VerifyResponse(results=[check.result for check in checks])


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
    "/v0/materials",
    response_model=MaterialsResponse,
    responses={500: {"model": ErrorResponse}, 503: {"model": ErrorResponse}},
)
async def materials(x_trace_id: TraceIdHeader) -> MaterialsResponse:
    del x_trace_id
    repository = get_knowledge_repository()
    if repository is None:
        raise _internal_error("knowledge database is not configured")
    try:
        snapshot = await repository.get_current_normative_snapshot()
        if snapshot is None:
            raise UnknownSnapshotError("no current normative snapshot")
        rows = await repository.list_snapshot_materials(snapshot.snapshot_id)
    except UnknownSnapshotError as exc:
        raise _internal_error("no current normative snapshot") from exc
    except Exception as exc:  # pragma: no cover - depends on external DB
        raise _internal_error("unable to list materials") from exc
    return MaterialsResponse(
        snapshot_id=snapshot.snapshot_id,
        materials=[
            MaterialSummary(
                document_id=row["document_id"],
                title=_safe_title(row["original_filename"]),
                declared_version=row["declared_version"],
                declared_date=row["declared_date"].isoformat() if row["declared_date"] else None,
                page_count=row["page_count"],
                fragment_count=row["fragment_count"],
            )
            for row in rows
        ],
    )


@router.get(
    "/v0/materials/{document_id}/sections",
    response_model=MaterialSectionsResponse,
    responses={404: {"model": ErrorResponse}, 500: {"model": ErrorResponse}, 503: {"model": ErrorResponse}},
)
async def material_sections(document_id: str, x_trace_id: TraceIdHeader) -> MaterialSectionsResponse:
    del x_trace_id
    repository = get_knowledge_repository()
    if repository is None:
        raise _internal_error("knowledge database is not configured")
    try:
        snapshot = await repository.get_current_normative_snapshot()
        if snapshot is None:
            raise UnknownSnapshotError("no current normative snapshot")
        rows = await repository.list_document_sections(snapshot.snapshot_id, document_id)
    except UnknownDocumentError as exc:
        from tenderhack_knowledge.contracts.v0 import ErrorCode

        raise HTTPException(
            status_code=404,
            detail={"code": ErrorCode.UNKNOWN_DOCUMENT.value, "message": f"Unknown document: {document_id}"},
        ) from exc
    except UnknownSnapshotError as exc:
        raise _internal_error("no current normative snapshot") from exc
    except Exception as exc:  # pragma: no cover - depends on external DB
        raise _internal_error("unable to list document sections") from exc
    return MaterialSectionsResponse(
        snapshot_id=snapshot.snapshot_id,
        document_id=document_id,
        sections=[
            MaterialSection(
                section=row["section"],
                page_start=row["page_start"],
                page_end=row["page_end"],
                first_fragment_id=row["first_fragment_id"],
            )
            for row in rows
        ],
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
async def quality_turn(payload: QualityTurnPush, x_trace_id: TraceIdHeader) -> AcceptedResponse:
    del x_trace_id
    repository = get_quality_repository()
    if repository is not None:
        try:
            await repository.ingest_turn(payload)
        except QualityPayloadConflict as exc:
            # knowledge-v0.md §4: never a 409 here — a conflicting payload under the same
            # (turn_id, revision) is an api-worker bug, not a legitimate concurrent write.
            raise _internal_error("conflicting quality turn payload for the same (turn_id, revision)") from exc
    return AcceptedResponse(status="stored")


@router.post(
    "/v0/quality/feedback",
    response_model=AcceptedResponse,
    status_code=202,
    responses=QUALITY_ERRORS,
)
async def quality_feedback(payload: QualityFeedbackPush, x_trace_id: TraceIdHeader) -> AcceptedResponse:
    del x_trace_id
    repository = get_quality_repository()
    if repository is not None:
        try:
            await repository.ingest_feedback(payload)
        except QualityPayloadConflict as exc:
            raise _internal_error("conflicting quality feedback payload for the same feedback_id") from exc
    return AcceptedResponse(status="stored")


@router.post(
    "/v0/quality/completions",
    response_model=AcceptedResponse,
    status_code=202,
    responses=QUALITY_ERRORS,
)
async def quality_completion(payload: QualityCompletionPush, x_trace_id: TraceIdHeader) -> AcceptedResponse:
    del x_trace_id
    repository = get_quality_repository()
    if repository is not None:
        try:
            await repository.ingest_completion(payload)
        except QualityPayloadConflict as exc:
            raise _internal_error("conflicting quality completion payload for the same case_id") from exc
    return AcceptedResponse(status="stored")


@router.get(
    "/v0/quality/evaluations",
    response_model=QualityEvaluationsResponse,
    responses={400: {"model": ErrorResponse}, 422: {"model": ErrorResponse}, 500: {"model": ErrorResponse}},
)
async def quality_evaluations(x_trace_id: TraceIdHeader, case_id: str = Query(...)) -> QualityEvaluationsResponse:
    del x_trace_id
    repository = get_quality_repository()
    if repository is None:
        return QualityEvaluationsResponse(case_id=case_id, evaluations=[])
    try:
        records = await repository.evaluations_for_case(case_id)
    except Exception as exc:  # pragma: no cover - depends on external DB
        raise _internal_error("unable to load quality evaluations") from exc
    return QualityEvaluationsResponse(case_id=case_id, evaluations=[record.to_contract() for record in records])


@router.get(
    "/v0/quality/issue-groups",
    response_model=IssueGroupsResponse,
    responses={400: {"model": ErrorResponse}, 422: {"model": ErrorResponse}, 500: {"model": ErrorResponse}},
)
async def issue_groups(x_trace_id: TraceIdHeader) -> IssueGroupsResponse:
    del x_trace_id
    repository = get_quality_repository()
    if repository is None:
        return IssueGroupsResponse(groups=[])
    try:
        groups = await repository.issue_groups_query()
    except Exception as exc:  # pragma: no cover - depends on external DB
        raise _internal_error("unable to load quality issue groups") from exc
    return IssueGroupsResponse(groups=[group.to_contract() for group in groups])
