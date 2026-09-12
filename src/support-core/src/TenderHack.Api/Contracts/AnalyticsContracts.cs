using TenderHack.Application.Knowledge;

namespace TenderHack.Api.Contracts;

/// <summary>
/// Read-only proxy shapes for `knowledge`'s quality endpoints (knowledge-v0.md §5). `Value` stays a
/// plain string — see <see cref="Application.Knowledge.DimensionScore"/> for why.
/// </summary>
public sealed record DimensionScoreView(string Value, string? Evidence, string? Reason, string? Limitation, string? ImprovementSuggestion);

public sealed record EvaluationView(
    string TurnId,
    DimensionScoreView FactualSupport,
    DimensionScoreView Completeness,
    DimensionScoreView Clarity,
    DimensionScoreView NextStep,
    bool CriticalError,
    string? CriticalErrorReason,
    DateTimeOffset EvaluatedAt);

public sealed record QualityEvaluationsResponse(string CaseId, IReadOnlyList<EvaluationView> Evaluations);

public sealed record HypothesisView(HypothesisType Type, string Text, HypothesisConfidence? Confidence);

public sealed record IssueGroupView(
    string GroupId,
    string Label,
    int N,
    IReadOnlyList<string> RepresentativeCaseIds,
    int? NegativeSignalCount,
    int? UnresolvedCount,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<HypothesisView> Hypotheses);

public sealed record IssueGroupsResponse(IReadOnlyList<IssueGroupView> Groups);
