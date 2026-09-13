using System.Text.Json;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Api.Contracts;

public static class CaseMapper
{
    private const int MaxCaseTitleLength = 36;

    public static CaseListItemResponse ToListItem(Case @case, int unreadNotifications = 0, string? firstUserMessage = null) =>
        new(@case.Id.ToString(), BuildCaseTitle(firstUserMessage), @case.ConversationStatus, @case.ResolutionStatus, @case.LastActivityAt, unreadNotifications);

    /// <summary>A stable, compact preview for the sidebar. It is derived from the persisted first
    /// USER_MESSAGE and never stored as a second source of truth.</summary>
    public static string BuildCaseTitle(string? firstUserMessage)
    {
        if (string.IsNullOrWhiteSpace(firstUserMessage))
        {
            return "Новый чат";
        }

        var normalized = string.Join(' ', firstUserMessage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidate = string.Join(' ', words.Take(6));
        var truncated = words.Length > 6;

        if (candidate.Length > MaxCaseTitleLength)
        {
            candidate = candidate[..(MaxCaseTitleLength - 1)].TrimEnd();
            var lastSpace = candidate.LastIndexOf(' ');
            if (lastSpace >= 12)
            {
                candidate = candidate[..lastSpace];
            }
            truncated = true;
        }

        return truncated ? $"{candidate}…" : candidate;
    }

    public static CaseSnapshotResponse ToSnapshot(Case @case, IReadOnlyList<PersistedCaseEvent> events, TenderHack.Domain.Feedback.Feedback? feedback = null)
    {
        var activeTurn = @case.ActiveTurn is { } turn
            ? new ActiveTurnView(turn.Id.ToString(), turn.Revision, turn.Status)
            : null;

        var lastDecision = @case.Turns.LastOrDefault(t => t.Decision is not null)?.Decision;

        return new CaseSnapshotResponse(
            @case.Id.ToString(),
            @case.ConversationStatus,
            @case.ResolutionStatus,
            lastDecision,
            @case.ModerationWarningCount,
            activeTurn,
            ToHandoffView(@case.Handoff),
            Timeline: [.. events.Select(ToTimelineItem)],
            LastEventId: (events.Count > 0 ? events[^1].EventId : 0).ToString(),
            @case.CompletedAt,
            @case.CompletionReason,
            ToFeedbackView(feedback));
    }

    public static FeedbackView? ToFeedbackView(TenderHack.Domain.Feedback.Feedback? feedback) => feedback is { } f
        ? new FeedbackView(f.SpecialistRating, f.InformationQualityRating, f.Solved, f.CommentText, f.SubmittedAt)
        : null;

    public static HandoffView? ToHandoffView(TenderHack.Domain.Handoffs.Handoff? handoff)
    {
        if (handoff is null)
        {
            return null;
        }

        return new HandoffView(
            handoff.Status,
            handoff.IntegrationMode,
            handoff.ExternalCaseId,
            handoff.AssignedSpecialist is { } specialist ? new HandoffSpecialistView(specialist.Ref, specialist.DisplayName) : null,
            handoff.Stage is { } stage ? new HandoffStageView(stage.Code, stage.DisplayName) : null,
            handoff.Terminal,
            handoff.Stale,
            handoff.AcceptedAt);
    }

    public static TimelineItemResponse ToTimelineItem(PersistedCaseEvent e) =>
        new(e.EventId.ToString(), e.Type, e.OccurredAt, e.TurnId?.ToString(), JsonDocument.Parse(e.PayloadJson).RootElement);

    public static CaseEventResponse ToCaseEvent(CaseId caseId, PersistedCaseEvent e) =>
        new(e.EventId.ToString(), caseId.ToString(), e.TurnId?.ToString(), e.Revision, e.Type, e.OccurredAt, JsonDocument.Parse(e.PayloadJson).RootElement);

    public static SendMessageResponse ToSendMessageResponse(string caseId, TurnOutcome outcome) =>
        new(
            caseId,
            outcome.TurnId.ToString(),
            outcome.Revision,
            outcome.FailureCategory is not null ? TurnStatus.Failed : TurnStatus.Completed,
            outcome.Decision,
            outcome.AnswerMarkdown is { } markdown
                ? new AnswerView(markdown, [.. (outcome.Sources ?? []).Select(ToSourceRefView)])
                : null,
            outcome.MissingConditions,
            outcome.FailureCategory);

    private static SourceRefView ToSourceRefView(AnswerSource source) =>
        new(source.FragmentId, source.Title, source.Page, "Открыть источник");
}
