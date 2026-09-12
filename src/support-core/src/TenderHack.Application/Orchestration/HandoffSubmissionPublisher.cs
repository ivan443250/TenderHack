using TenderHack.Application.Ports;
using TenderHack.Domain;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Application.Orchestration;

/// <summary>
/// Emits the timeline event + owner notification for the adapter's answer to a submission
/// (`api-worker`, after <see cref="IHandoffAdapter.SubmitAsync"/>): web-api-v0 §4.2 makes acceptance
/// and failure a `HANDOFF_STATUS` with `changed: ["status"]`, and architecture.md §5.9 maps
/// acceptance to `HANDOFF_UPDATED`. Without this the browser would sit on `PENDING` until the first
/// polled stage arrived — or forever, for `FAILED`.
/// </summary>
public sealed class HandoffSubmissionPublisher(ITurnEventStream events, INotificationSink notifications)
{
    public async Task PublishAsync(Case @case, string? safeMessage, DateTimeOffset now, CancellationToken ct)
    {
        var handoff = @case.Handoff ?? throw new HandoffNotFoundException(@case.Id);

        var eventId = await events.PublishAsync(@case.Id, new CaseEvent("HANDOFF_STATUS", null, null, now, new Dictionary<string, object?>
        {
            ["status"] = handoff.Status.ToString(),
            ["integration_mode"] = handoff.IntegrationMode?.ToString(),
            ["external_case_id"] = handoff.ExternalCaseId,
            ["stage"] = null,
            ["assigned_specialist"] = null,
            ["terminal"] = null,
            ["safe_message"] = safeMessage,
            ["changed"] = new[] { "status" },
        }), ct);

        var (title, body) = handoff.Status switch
        {
            HandoffStatus.Accepted => ("Обращение передано в поддержку", "Заявка принята. Этап и специалист появятся, когда их сообщит поддержка."),
            HandoffStatus.SimulatedAccepted => ("Обращение передано в поддержку (демо)", "Демо-адаптер принял заявку. Этапы будут приходить по расписанию демо-сценария."),
            HandoffStatus.Failed => ("Не удалось передать обращение", safeMessage ?? "Поддержка не подтвердила приём. Можно повторить отправку."),
            _ => ("Обновление по обращению", "Статус передачи изменён."),
        };

        notifications.Enqueue(@case.OwnerId, @case.Id, "HANDOFF_UPDATED", title, body, handoff.IntegrationMode?.ToString(), eventId);
    }
}
