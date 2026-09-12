using TenderHack.Domain.Feedback;

namespace TenderHack.Application.Knowledge;

/// <summary>knowledge-v0.md §10 — pushed asynchronously by `api-worker` via outbox, at-least-once, idempotent.</summary>
public sealed record StageTimings(int? UnderstandMs, int? RetrieveMs, int? DraftMs, int? VerifyMs);

/// <summary>
/// `Decision`, `ReasonCodes`, `HandoffStatus`, `RecommendedLine`, `ServiceNeed` are opaque string
/// labels produced by `api` — `knowledge` never interprets or reproduces their policy.
/// </summary>
public sealed record QualityTurnPush(
    string CaseId,
    string TurnId,
    int Revision,
    DateTimeOffset OccurredAt,
    string QuestionText,
    string? PriorTurnsSummary,
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string? AnswerMarkdown,
    IReadOnlyList<string>? EvidenceFragmentIds,
    string? SnapshotId,
    string? HandoffStatus,
    string? RecommendedLine,
    string? ServiceNeed,
    StageTimings StageTimings,
    string? ErrorCategory);

/// <summary>
/// `helpful` is deprecated wire compatibility, mirroring `InformationQualityRating`; this port never
/// sets it — the generated client's own mapping fills it in for the wire.
/// </summary>
public sealed record QualityFeedbackPush(
    string FeedbackId,
    string CaseId,
    string TurnId,
    DateTimeOffset OccurredAt,
    FeedbackRating? SpecialistRating,
    FeedbackRating? InformationQualityRating,
    string? SpecialistRef,
    string? IntegrationMode,
    bool? Solved,
    string? CommentText);

public sealed record QualityCompletionPush(
    string CaseId,
    DateTimeOffset CompletedAt,
    string CompletionReason,
    string ResolutionStatus,
    string? HandoffStatus,
    string? IntegrationMode,
    string? SpecialistRef,
    string? StageCode,
    int? ModerationWarningCount,
    int? TurnCount);

/// <summary>Wire values are literally `"0" | "1" | "2" | "UNKNOWN" | "NOT_APPLICABLE"` — kept as a
/// plain string rather than a C# enum since the digits don't map through the global SCREAMING_SNAKE
/// enum-naming policy Api uses elsewhere; `knowledge` owns this vocabulary, `api` only displays it.</summary>
public sealed record DimensionScore(string Value, string? Evidence, string? Reason, string? Limitation, string? ImprovementSuggestion);

public sealed record Evaluation(
    string TurnId,
    DimensionScore FactualSupport,
    DimensionScore Completeness,
    DimensionScore Clarity,
    DimensionScore NextStep,
    bool CriticalError,
    string? CriticalErrorReason,
    DateTimeOffset EvaluatedAt);

public sealed record QualityEvaluations(string CaseId, IReadOnlyList<Evaluation> Evaluations);

public enum HypothesisType
{
    KnowledgeGap,
    ProcessIssue,
    PortalIssue,
    Other,
}

public enum HypothesisConfidence
{
    Low,
    Medium,
    High,
}

public sealed record Hypothesis(HypothesisType Type, string Text, HypothesisConfidence? Confidence);

public sealed record IssueGroup(
    string GroupId,
    string Label,
    int N,
    IReadOnlyList<string> RepresentativeCaseIds,
    int? NegativeSignalCount,
    int? UnresolvedCount,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<Hypothesis> Hypotheses);

public sealed record IssueGroups(IReadOnlyList<IssueGroup> Groups);
