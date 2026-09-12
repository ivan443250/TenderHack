using TenderHack.Application.Ports;

namespace TenderHack.Application.UseCases;

public sealed class ListNotificationsUseCase(INotificationReader notifications)
{
    public Task<IReadOnlyList<NotificationEntry>> ExecuteAsync(string ownerId, long after, bool unreadOnly, CancellationToken ct) =>
        notifications.ListAsync(ownerId, after, unreadOnly, ct);
}
