using System.Text.Json;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Api.Contracts;

public static class CaseMapper
{
    public static CaseListItemResponse ToListItem(Case @case) =>
        new(@case.Id.ToString(), @case.ConversationStatus, @case.ResolutionStatus, @case.LastActivityAt, UnreadNotifications: 0);

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

    /// <summary>
    /// Re-derives routing from the case's own last `HANDOFF_OFFER` event rather than trusting the
    /// client — the frontend only ever gets to edit the human-written summary text.
    /// </summary>
    public static RoutingFacts? ExtractLastRouting(IReadOnlyList<PersistedCaseEvent> events)
    {
        var last = events.LastOrDefault(e => e.Type == "HANDOFF_OFFER");
        if (last is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(last.PayloadJson);
        var root = document.RootElement;
        var dispatchQueue = root.GetProperty("dispatch_queue").GetString() ?? "l1-general";
        var reasonCodes = root.GetProperty("reason_codes").EnumerateArray().Select(e => e.GetString()!).ToArray();
        var engineeringReviewSuggested = root.GetProperty("engineering_review_suggested").GetBoolean();
        return new RoutingFacts(dispatchQueue, reasonCodes, engineeringReviewSuggested);
    }

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

public sealed record RoutingFacts(string DispatchQueue, IReadOnlyList<string> ReasonCodes, bool EngineeringReviewSuggested);
