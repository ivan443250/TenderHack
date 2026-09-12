from datetime import datetime, timezone
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field


class ContractInput(BaseModel):
    model_config = ConfigDict(extra="allow")


class HealthResponse(BaseModel):
    status: Literal["ok"]
    model_versions: dict[str, str] | None = None


class Entity(BaseModel):
    type: str
    value: str
    provenance: Literal["user_explicit", "trusted_portal_context", "inferred", "unknown"]


class UnderstandResponse(BaseModel):
    normalized_text: str
    entities: list[Entity]
    exact_codes: list[str]
    language_flags: dict[str, Any]


class ModerationContextResponse(BaseModel):
    ambiguity: Literal["OFFENSIVE", "NOT_OFFENSIVE", "UNCERTAIN"]
    model_version: str


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
    scores: CandidateScores = Field(default_factory=CandidateScores)
    applicability_flags: list[str] = Field(default_factory=list)


class RetrieveResponse(BaseModel):
    snapshot_id: str
    retrieval_config_version: str
    candidates: list[Candidate]


class AnswerabilityResponse(BaseModel):
    evidence_sufficiency: Literal["SUFFICIENT", "CONDITION_DEPENDENT", "INSUFFICIENT"]
    evidence_fragment_ids: list[str]
    missing_conditions: list[str]
    risk_flags: list[str]


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
    created_at: str
    document_count: int
    fragment_count: int


class AcceptedResponse(BaseModel):
    status: Literal["stored"]


class DimensionScore(BaseModel):
    value: Literal["0", "1", "2", "UNKNOWN", "NOT_APPLICABLE"]
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
    evaluated_at: str


class QualityEvaluationsResponse(BaseModel):
    case_id: str
    evaluations: list[Evaluation]


class Hypothesis(BaseModel):
    type: Literal["KNOWLEDGE_GAP", "PROCESS_ISSUE", "PORTAL_ISSUE", "OTHER"]
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


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()
