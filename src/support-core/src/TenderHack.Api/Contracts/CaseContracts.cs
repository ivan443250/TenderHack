using System.Text.Json;
using TenderHack.Application.Knowledge;
using TenderHack.Domain.Cases;

namespace TenderHack.Api.Contracts;

/// <summary>
/// Wire shapes for web-api-v0.md. Property names go through the configured snake_case naming
/// policy; Domain enums go through a SCREAMING_SNAKE_CASE converter — both configured once in
/// Program.cs, so these records just use the real Domain types.
/// </summary>
public sealed record CaseListItemResponse(
    string CaseId,
    ConversationStatus ConversationStatus,
    ResolutionStatus ResolutionStatus,
    DateTimeOffset LastActivityAt,
    int UnreadNotifications);

public sealed record ActiveTurnView(string TurnId, int Revision, TurnStatus Status);

public sealed record CaseSnapshotResponse(
    string CaseId,
    ConversationStatus ConversationStatus,
    ResolutionStatus ResolutionStatus,
    Decision? LastDecision,
    int ModerationWarningCount,
    ActiveTurnView? ActiveTurn,
    object? Handoff,
    IReadOnlyList<TimelineItemResponse> Timeline,
    string LastEventId,
    DateTimeOffset? CompletedAt,
    CompletionReason? CompletionReason,
    object? Feedback);

public sealed record TimelineItemResponse(
    string ItemId,
    string Type,
    DateTimeOffset OccurredAt,
    string? TurnId,
    JsonElement Payload);

public sealed record SendMessageRequest(string Text, string? ClientMessageId);

public sealed record AnswerView(string Markdown, IReadOnlyList<SourceRefView> Sources);

public sealed record SourceRefView(string FragmentId);

public sealed record SendMessageResponse(
    string CaseId,
    string TurnId,
    int Revision,
    TurnStatus Status,
    Decision Decision,
    AnswerView? Answer,
    IReadOnlyList<string>? MissingConditions,
    KnowledgeFailureCategory? FailureCategory);

public sealed record SourceResponse(
    string DocumentId,
    string Title,
    string Version,
    int? Page,
    string? Anchor,
    string Text,
    string SnapshotId);
