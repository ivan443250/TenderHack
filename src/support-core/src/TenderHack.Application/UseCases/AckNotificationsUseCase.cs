using TenderHack.Application.Ports;

namespace TenderHack.Application.UseCases;

/// <summary>Idempotent — acking an already-read or unknown id is a no-op (web-api-v0.md §12).</summary>
public sealed class AckNotificationsUseCase(INotificationReader notifications)
{
    public Task ExecuteAsync(string ownerId, IReadOnlyList<long> notificationIds, CancellationToken ct) =>
        notifications.AckAsync(ownerId, notificationIds, ct);
}
