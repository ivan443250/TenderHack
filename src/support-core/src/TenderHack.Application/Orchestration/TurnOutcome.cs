using TenderHack.Application.Knowledge;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.Orchestration;

/// <summary>
/// What one <see cref="TurnOrchestrator.RunAsync"/> call produced, shaped for `TenderHack.Api` to
/// render — never a knowledge DTO, never a raw exception.
/// </summary>
public sealed record TurnOutcome(
    TurnId TurnId,
    int Revision,
    Decision Decision,
    int ModerationWarningCount,
    string? AnswerMarkdown = null,
    IReadOnlyList<string>? SourceFragmentIds = null,
    IReadOnlyList<string>? MissingConditions = null,
    KnowledgeFailureCategory? FailureCategory = null)
{
    public static TurnOutcome Moderation(TurnId turnId, int revision, Decision decision, int warningCount) =>
        new(turnId, revision, decision, warningCount);

    public static TurnOutcome Answered(TurnId turnId, int revision, int warningCount, string markdown, IReadOnlyList<string> sourceFragmentIds) =>
        new(turnId, revision, Decision.Answer, warningCount, AnswerMarkdown: markdown, SourceFragmentIds: sourceFragmentIds);

    public static TurnOutcome Clarify(TurnId turnId, int revision, int warningCount, IReadOnlyList<string> missingConditions) =>
        new(turnId, revision, Decision.Clarify, warningCount, MissingConditions: missingConditions);

    public static TurnOutcome HandoffOffer(TurnId turnId, int revision, int warningCount) =>
        new(turnId, revision, Decision.HandoffOffer, warningCount);

    public static TurnOutcome TechnicalError(TurnId turnId, int revision, int warningCount, KnowledgeFailureCategory category) =>
        new(turnId, revision, Decision.TechnicalError, warningCount, FailureCategory: category);
}
