using TenderHack.Domain.Cases;

namespace TenderHack.Application.Ports;

/// <summary>
/// Owner-scoped derived facts (architecture.md §5.9): written alongside the case state change that
/// causes them, read back independently of which case is open.
/// </summary>
public interface INotificationSink
{
    void Enqueue(string ownerId, CaseId caseId, string type, string title, string body, string? integrationMode);
}

public interface INotificationReader
{
    /// <summary>Notifications with `NotificationId &gt; after`, ordered ascending; `unreadOnly` filters to `ReadAt == null`.</summary>
    Task<IReadOnlyList<NotificationEntry>> ListAsync(string ownerId, long after, bool unreadOnly, CancellationToken ct);

    Task AckAsync(string ownerId, IReadOnlyList<long> notificationIds, CancellationToken ct);
}

public sealed record NotificationEntry(
    long NotificationId,
    CaseId CaseId,
    string Type,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ReadAt,
    string Title,
    string Body,
    string? IntegrationMode);
