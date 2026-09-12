using System.Text.Json;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Api.Contracts;

public static class CaseMapper
{
    public static CaseListItemResponse ToListItem(Case @case) =>
        new(@case.Id.ToString(), @case.ConversationStatus, @case.ResolutionStatus, @case.LastActivityAt, UnreadNotifications: 0);

    public static CaseSnapshotResponse ToSnapshot(Case @case, IReadOnlyList<PersistedCaseEvent> events)
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
            Handoff: null,
            Timeline: [.. events.Select(ToTimelineItem)],
            LastEventId: (events.Count > 0 ? events[^1].EventId : 0).ToString(),
            @case.CompletedAt,
            @case.CompletionReason,
            Feedback: null);
    }

    public static TimelineItemResponse ToTimelineItem(PersistedCaseEvent e) =>
        new(e.EventId.ToString(), e.Type, e.OccurredAt, e.TurnId?.ToString(), JsonDocument.Parse(e.PayloadJson).RootElement);

    public static SendMessageResponse ToSendMessageResponse(string caseId, TurnOutcome outcome) =>
        new(
            caseId,
            outcome.TurnId.ToString(),
            outcome.Revision,
            outcome.FailureCategory is not null ? TurnStatus.Failed : TurnStatus.Completed,
            outcome.Decision,
            outcome.AnswerMarkdown is { } markdown
                ? new AnswerView(markdown, [.. (outcome.SourceFragmentIds ?? []).Select(id => new SourceRefView(id))])
                : null,
            outcome.MissingConditions,
            outcome.FailureCategory);
}
