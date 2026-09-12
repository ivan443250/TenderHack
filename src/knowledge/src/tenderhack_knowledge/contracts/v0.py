"""Typed wire DTOs for the frozen knowledge-v0 boundary.

The frozen object schemas do not close additional properties.  The models
therefore keep Pydantic's default ``extra='ignore'`` behaviour: additive input
fields are tolerated, while response DTOs never echo fields they do not own.
"""

from datetime import datetime, timezone
from enum import Enum
from typing import Any, Literal

from pydantic import BaseModel, Field


class ErrorCode(str, Enum):
    VALIDATION_ERROR = "VALIDATION_ERROR"
    MISSING_TRACE_HEADER = "MISSING_TRACE_HEADER"
    UNKNOWN_SNAPSHOT = "UNKNOWN_SNAPSHOT"
    UNKNOWN_FRAGMENT = "UNKNOWN_FRAGMENT"
    MODEL_UNAVAILABLE = "MODEL_UNAVAILABLE"
    MODEL_ERROR = "MODEL_ERROR"
    INTERNAL_ERROR = "INTERNAL_ERROR"


class ErrorResponse(BaseModel):
    code: ErrorCode
    message: str
    detail: dict[str, Any] | None = None


class HealthResponse(BaseModel):
    status: Literal["ok"]
    model_versions: dict[str, str] | None = None


class EntityProvenance(str, Enum):
    USER_EXPLICIT = "user_explicit"
    TRUSTED_PORTAL_CONTEXT = "trusted_portal_context"
    INFERRED = "inferred"
    UNKNOWN = "unknown"


class Entity(BaseModel):
    type: str
    value: str
    provenance: EntityProvenance


class LanguageFlags(BaseModel):
    detected_language: str
    typo_corrected: bool


class UnderstandRequest(BaseModel):
    text: str
    prior_turn_summary: str | None = None


class UnderstandResponse(BaseModel):
    normalized_text: str
    entities: list[Entity]
    exact_codes: list[str]
    language_flags: LanguageFlags


class ModerationAmbiguity(str, Enum):
    OFFENSIVE = "OFFENSIVE"
    NOT_OFFENSIVE = "NOT_OFFENSIVE"
    UNCERTAIN = "UNCERTAIN"


class MatchedSpan(BaseModel):
    start: int
    end: int


class RuleMatch(BaseModel):
    rule_id: str
    version: str
    matched_term: str
    matched_span: MatchedSpan


class ModerationContextRequest(BaseModel):
    text: str
    rule_match: RuleMatch


class ModerationContextResponse(BaseModel):
    ambiguity: ModerationAmbiguity
    model_version: str


class Corpus(str, Enum):
    NORMATIVE = "NORMATIVE"
    HISTORICAL = "HISTORICAL"


class RetrieveRequest(BaseModel):
    query: str
    entities: list[Entity]
    exact_codes: list[str]
    corpus: Corpus
    snapshot_id: str | None = None


class CandidateScores(BaseModel):
    exact: float | None = None
    fts: float | None = None
    dense: float | None = None
    rerank: float | None = None


class Candidate(BaseModel):
    fragment_id: str
    document_id: str
    page: int | None = None
    anchor: str | None = None
    scores: CandidateScores
    applicability_flags: list[str]


class RetrieveResponse(BaseModel):
    snapshot_id: str
    retrieval_config_version: str
    candidates: list[Candidate]


class AnswerabilityRequest(BaseModel):
    query: str
    snapshot_id: str
    candidate_fragment_ids: list[str]


class EvidenceSufficiency(str, Enum):
    SUFFICIENT = "SUFFICIENT"
    CONDITION_DEPENDENT = "CONDITION_DEPENDENT"
    INSUFFICIENT = "INSUFFICIENT"


class AnswerabilityResponse(BaseModel):
    evidence_sufficiency: EvidenceSufficiency
    evidence_fragment_ids: list[str]
    missing_conditions: list[str]
    risk_flags: list[str]


class DraftConstraints(BaseModel):
    max_output_tokens: int = 800
    tone: str | None = None


class DraftRequest(BaseModel):
    query: str
    snapshot_id: str
    evidence_fragment_ids: list[str]
    constraints: DraftConstraints


class Claim(BaseModel):
    claim_id: str
    text: str
    fragment_ids: list[str]


class TokenUsage(BaseModel):
    input: int
    output: int


class DraftResponse(BaseModel):
    draft_markdown: str
    claims: list[Claim]
    model_version: str
    token_usage: TokenUsage


class VerifyRequest(BaseModel):
    claims: list[Claim]
    snapshot_id: str


class VerifyResult(BaseModel):
    claim_id: str
    supported: bool
    evidence_fragment_ids: list[str]


class VerifyResponse(BaseModel):
    results: list[VerifyResult]


class SourceResponse(BaseModel):
    document_id: str
    title: str
    version: str
    page: int | None = None
    anchor: str | None = None
    text: str
    snapshot_id: str


class SnapshotResponse(BaseModel):
    snapshot_id: str
    created_at: datetime
    document_count: int
    fragment_count: int


class AcceptedResponse(BaseModel):
    status: Literal["stored"]


class StageTimings(BaseModel):
    understand_ms: int | None = None
    retrieve_ms: int | None = None
    draft_ms: int | None = None
    verify_ms: int | None = None


class QualityTurnPush(BaseModel):
    case_id: str
    turn_id: str
    revision: int
    occurred_at: datetime
    question_text: str
    prior_turns_summary: str | None = None
    decision: str
    reason_codes: list[str]
    answer_markdown: str | None = None
    evidence_fragment_ids: list[str] | None = None
    snapshot_id: str | None = None
    handoff_status: str | None = None
    recommended_line: str | None = None
    service_need: str | None = None
    stage_timings: StageTimings
    error_category: str | None = None
    # Additive v0 fields (2026-09-12, knowledge-v0.openapi.yaml QualityTurnPush): the generator's
    # model_version / this turn's retrieval_config_version, when a draft/retrieve actually ran.
    model_version: str | None = None
    retrieval_config_version: str | None = None


class FeedbackRating(str, Enum):
    POSITIVE = "POSITIVE"
    NEGATIVE = "NEGATIVE"


class IntegrationMode(str, Enum):
    REAL = "REAL"
    SIMULATED = "SIMULATED"


class QualityFeedbackPush(BaseModel):
    feedback_id: str
    case_id: str
    turn_id: str
    occurred_at: datetime
    helpful: bool | None = Field(default=None, deprecated=True)
    specialist_rating: FeedbackRating | None = None
    information_quality_rating: FeedbackRating | None = None
    specialist_ref: str | None = None
    integration_mode: IntegrationMode | None = None
    solved: bool | None = None
    comment_text: str | None = None


class QualityCompletionPush(BaseModel):
    case_id: str
    completed_at: datetime
    completion_reason: str
    resolution_status: str
    handoff_status: str | None = None
    integration_mode: IntegrationMode | None = None
    specialist_ref: str | None = None
    stage_code: str | None = None
    moderation_warning_count: int | None = None
    turn_count: int | None = None


class DimensionValue(str, Enum):
    ZERO = "0"
    ONE = "1"
    TWO = "2"
    UNKNOWN = "UNKNOWN"
    NOT_APPLICABLE = "NOT_APPLICABLE"


class DimensionScore(BaseModel):
    value: DimensionValue
    evidence: str | None = None
    reason: str | None = None
    limitation: str | None = None
    improvement_suggestion: str | None = None


class Evaluation(BaseModel):
    turn_id: str
    factual_support: DimensionScore
    completeness: DimensionScore
    clarity: DimensionScore
    next_step: DimensionScore
    critical_error: bool
    critical_error_reason: str | None = None
    evaluated_at: datetime


class QualityEvaluationsResponse(BaseModel):
    case_id: str
    evaluations: list[Evaluation]


class HypothesisType(str, Enum):
    KNOWLEDGE_GAP = "KNOWLEDGE_GAP"
    PROCESS_ISSUE = "PROCESS_ISSUE"
    PORTAL_ISSUE = "PORTAL_ISSUE"
    OTHER = "OTHER"


class Hypothesis(BaseModel):
    type: HypothesisType
    text: str
    confidence: Literal["LOW", "MEDIUM", "HIGH"] | None = None


class IssueGroup(BaseModel):
    group_id: str
    label: str
    n: int
    representative_case_ids: list[str]
    negative_signal_count: int | None = None
    unresolved_count: int | None = None
    limitations: list[str]
    hypotheses: list[Hypothesis]


class IssueGroupsResponse(BaseModel):
    groups: list[IssueGroup]


def now_iso() -> datetime:
    return datetime.now(timezone.utc)
