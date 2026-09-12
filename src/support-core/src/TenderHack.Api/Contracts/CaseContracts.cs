using System.Text.Json;
using TenderHack.Application.Knowledge;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Feedback;
using TenderHack.Domain.Handoffs;

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

/// <summary>web-api-v0.md §4.4. All fields after `Status`/`IntegrationMode` are optional — a null
/// stays null, the browser hides the row rather than showing a placeholder.</summary>
public sealed record HandoffView(
    HandoffStatus Status,
    IntegrationMode? IntegrationMode,
    string? ExternalCaseId,
    HandoffSpecialistView? AssignedSpecialist,
    HandoffStageView? Stage,
    HandoffTerminalOutcome? Terminal,
    bool Stale,
    DateTimeOffset? UpdatedAt);

public sealed record HandoffStageView(string Code, string? DisplayName);

public sealed record HandoffSpecialistView(string Ref, string? DisplayName);

public sealed record CaseSnapshotResponse(
    string CaseId,
    ConversationStatus ConversationStatus,
    ResolutionStatus ResolutionStatus,
    Decision? LastDecision,
    int ModerationWarningCount,
    ActiveTurnView? ActiveTurn,
    HandoffView? Handoff,
    IReadOnlyList<TimelineItemResponse> Timeline,
    string LastEventId,
    DateTimeOffset? CompletedAt,
    CompletionReason? CompletionReason,
    FeedbackView? Feedback);

public sealed record FeedbackView(
    FeedbackRating? SpecialistRating,
    FeedbackRating? InformationQualityRating,
    bool? Solved,
    string? CommentText,
    DateTimeOffset SubmittedAt);

public sealed record CompleteCaseRequest(bool? Solved);

public sealed record SubmitFeedbackRequest(
    FeedbackRating? SpecialistRating,
    FeedbackRating? InformationQualityRating,
    bool? Solved,
    string? CommentText);

/// <summary>web-api-v0.md §12 — the same shape on `GET /notifications` and on the `notification` SSE stream.</summary>
public sealed record NotificationResponse(
    string NotificationId,
    string CaseId,
    string Type,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ReadAt,
    NotificationPayload Payload);

public sealed record NotificationPayload(string Title, string Body, string? IntegrationMode);

public sealed record AckNotificationsRequest(IReadOnlyList<string>? Ids);

/// <summary>web-api-v0.md §4.2 — a `CaseSnapshot.timeline` entry and the `GET /events` catch-up row.</summary>
public sealed record TimelineItemResponse(
    string ItemId,
    string Type,
    DateTimeOffset OccurredAt,
    string? TurnId,
    JsonElement Payload);

/// <summary>web-api-v0.md §6 — the `data:` envelope of one `case_event` SSE message.</summary>
public sealed record CaseEventResponse(
    string EventId,
    string CaseId,
    string? TurnId,
    int? Revision,
    string Type,
    DateTimeOffset OccurredAt,
    JsonElement Payload);

public sealed record SendMessageRequest(string Text, string? ClientMessageId);

/// <summary>`Summary` is the only field the caller edits — `DispatchQueue`/`ReasonCodes`/
/// `EngineeringReviewSuggested` are re-derived server-side from the case's own last `HANDOFF_OFFER`
/// event, never trusted from the client (web-api-v0.md §5.5, "routing is a Domain policy").</summary>
public sealed record ConfirmHandoffRequest(string Summary);

public sealed record RetryHandoffRequest(string Summary);

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
