using TenderHack.Application.Knowledge;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.Orchestration;

/// <summary>
/// Shared completion-event/notification/quality-push emission for the two paths that can complete a
/// case (architecture.md §5.8): user-initiated (`CompleteCaseUseCase`) and support-initiated
/// (`IngestHandoffStatusUseCase`, on a terminal adapter fact). Never called for moderation close —
/// that path has its own event and never asks for feedback.
/// </summary>
public sealed class CaseCompletionPublisher(ITurnEventStream events, INotificationSink notifications, IOutbox outbox)
{
    public async Task PublishAsync(Case @case, ResolutionStatus resolutionBefore, DateTimeOffset now, CancellationToken ct)
    {
        if (@case.ResolutionStatus != resolutionBefore)
        {
            await events.PublishAsync(@case.Id, new CaseEvent("CASE_RESOLUTION_CHANGED", null, null, now,
                new Dictionary<string, object?> { ["resolution_status"] = @case.ResolutionStatus.ToString() }), ct);
        }

        var completedEventId = await events.PublishAsync(@case.Id, new CaseEvent("CASE_COMPLETED", null, null, now,
            new Dictionary<string, object?>
            {
                ["completion_reason"] = @case.CompletionReason?.ToString(),
                ["resolution_status"] = @case.ResolutionStatus.ToString(),
            }), ct);

        notifications.Enqueue(@case.OwnerId, @case.Id, "CASE_COMPLETED", "Обращение завершено",
            BuildCompletionBody(@case), integrationMode: null, completedEventId);

        outbox.Enqueue(QualityOutboxMessages.Completion, new QualityCompletionPush(
            @case.Id.ToString(),
            now,
            @case.CompletionReason?.ToString() ?? string.Empty,
            @case.ResolutionStatus.ToString(),
            HandoffStatus: @case.Handoff?.Status.ToString(),
            IntegrationMode: @case.Handoff?.IntegrationMode?.ToString(),
            SpecialistRef: @case.Handoff?.AssignedSpecialist?.Ref,
            StageCode: @case.Handoff?.Stage?.Code,
            ModerationWarningCount: @case.ModerationWarningCount,
            TurnCount: @case.Turns.Count));

        // Moderation close ends the case but never asks for feedback (product-spec.md §14).
        if (@case.CompletionReason == CompletionReason.Moderation)
        {
            return;
        }

        var feedbackRequestedEventId = await events.PublishAsync(@case.Id, new CaseEvent("FEEDBACK_REQUESTED", null, null, now, new Dictionary<string, object?>()), ct);
        notifications.Enqueue(@case.OwnerId, @case.Id, "FEEDBACK_REQUESTED", "Оцените обращение",
            "Расскажите, помогло ли решение — это займёт меньше минуты.", integrationMode: null, feedbackRequestedEventId);
    }

    private static string BuildCompletionBody(Case @case) => @case.CompletionReason switch
    {
        CompletionReason.Support => "Обращение завершено поддержкой.",
        CompletionReason.User => "Вы завершили обращение.",
        _ => "Обращение завершено.",
    };
}
