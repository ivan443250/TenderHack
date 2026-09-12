using TenderHack.Application.Exceptions;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Application.UseCases;

/// <summary>
/// Single write path for handoff status facts, whichever channel delivered them — polling or the
/// inbound webhook (support-adapter-v0.md §6). On a genuinely new revision it publishes
/// `HANDOFF_STATUS` + a `HANDOFF_UPDATED` notification, and — only the first time a terminal fact
/// closes an still-`ACTIVE` case — the shared completion events (architecture.md §6.3).
/// </summary>
public sealed class IngestHandoffStatusUseCase(
    ICaseRepository cases,
    IUnitOfWork unitOfWork,
    ITurnEventStream events,
    INotificationSink notifications,
    CaseCompletionPublisher completionPublisher,
    TimeProvider clock)
{
    public async Task ExecuteAsync(
        CaseId caseId,
        HandoffId handoffId,
        long externalRevision,
        HandoffStage? stage,
        AssignedSpecialist? assignedSpecialist,
        HandoffTerminalOutcome? terminal,
        CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new CaseNotFoundException(caseId);
        var conversationStatusBefore = @case.ConversationStatus;
        var resolutionBefore = @case.ResolutionStatus;
        var now = clock.GetUtcNow();

        var applied = @case.IngestHandoffStatus(handoffId, externalRevision, stage, assignedSpecialist, terminal, now);
        if (!applied)
        {
            // Duplicate (handoff-status-sync polling can re-observe a revision the webhook already
            // delivered, or vice versa) — no new fact, nothing to publish.
            return;
        }

        var changed = new List<string>();
        if (stage is not null) changed.Add("stage");
        if (assignedSpecialist is not null) changed.Add("assigned_specialist");
        if (terminal is not null) changed.Add("terminal");

        var handoff = @case.Handoff!;
        await events.PublishAsync(caseId, new CaseEvent("HANDOFF_STATUS", null, null, now, new Dictionary<string, object?>
        {
            ["status"] = handoff.Status.ToString(),
            ["integration_mode"] = handoff.IntegrationMode?.ToString(),
            ["stage"] = stage is { } s ? new { code = s.Code, display_name = s.DisplayName } : null,
            ["assigned_specialist"] = assignedSpecialist is { } a ? new { @ref = a.Ref, display_name = a.DisplayName } : null,
            ["terminal"] = terminal?.ToString(),
            ["changed"] = changed,
        }), ct);

        notifications.Enqueue(@case.OwnerId, caseId, "HANDOFF_UPDATED", "Обновление по обращению",
            BuildHandoffUpdateBody(stage, assignedSpecialist), handoff.IntegrationMode?.ToString());

        if (terminal is not null && conversationStatusBefore == ConversationStatus.Active)
        {
            await completionPublisher.PublishAsync(@case, resolutionBefore, now, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static string BuildHandoffUpdateBody(HandoffStage? stage, AssignedSpecialist? specialist)
    {
        if (specialist is { DisplayName: { } name })
        {
            return $"Обращение назначено специалисту: {name}.";
        }

        if (stage is { DisplayName: { } stageName })
        {
            return $"Статус обращения изменён: {stageName}.";
        }

        return "Статус обращения обновлён.";
    }
}
