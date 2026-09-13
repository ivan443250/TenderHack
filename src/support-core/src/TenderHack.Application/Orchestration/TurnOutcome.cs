using TenderHack.Application.Knowledge;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.Orchestration;

/// <summary>
/// web-api-v0.md §4.3 answer source shape. `Title` falls back to `DocumentId` when `knowledge`
/// did not supply one (`Candidate.title` is an additive, normally-null field) — the browser never
/// shows a blank source button.
/// </summary>
public sealed record AnswerSource(string FragmentId, string DocumentId, int? Page, string? Anchor, string Title);

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
    IReadOnlyList<AnswerSource>? Sources = null,
    IReadOnlyList<string>? MissingConditions = null,
    KnowledgeFailureCategory? FailureCategory = null,
    Guid TraceId = default)
{
    public static TurnOutcome Moderation(TurnId turnId, int revision, Decision decision, int warningCount) =>
        new(turnId, revision, decision, warningCount);

    public static TurnOutcome Answered(TurnId turnId, int revision, int warningCount, string markdown, IReadOnlyList<AnswerSource> sources) =>
        new(turnId, revision, Decision.Answer, warningCount, AnswerMarkdown: markdown, Sources: sources);

    public static TurnOutcome AnsweredWithHandoff(TurnId turnId, int revision, int warningCount, string markdown, IReadOnlyList<AnswerSource> sources) =>
        new(turnId, revision, Decision.AnswerAndHandoff, warningCount, AnswerMarkdown: markdown, Sources: sources);

    public static TurnOutcome Clarify(TurnId turnId, int revision, int warningCount, IReadOnlyList<string> missingConditions) =>
        new(turnId, revision, Decision.Clarify, warningCount, MissingConditions: missingConditions);

    public static TurnOutcome HandoffOffer(TurnId turnId, int revision, int warningCount) =>
        new(turnId, revision, Decision.HandoffOffer, warningCount);

    public static TurnOutcome OutOfScope(TurnId turnId, int revision, int warningCount) =>
        new(turnId, revision, Decision.OutOfScope, warningCount);

    public static TurnOutcome TechnicalError(TurnId turnId, int revision, int warningCount, KnowledgeFailureCategory category) =>
        new(turnId, revision, Decision.TechnicalError, warningCount, FailureCategory: category);
}
