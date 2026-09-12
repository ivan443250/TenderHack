using Microsoft.EntityFrameworkCore;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Infrastructure.Persistence;

/// <summary>
/// <see cref="Enqueue"/> stages the row on the ambient scoped `DbContext` — it commits together
/// with whatever case/handoff state the caller changed this call, via the caller's own
/// <see cref="IUnitOfWork.SaveChangesAsync"/>. <see cref="AckAsync"/> is a standalone read-endpoint
/// operation and commits itself.
/// </summary>
public sealed class NotificationStore(TenderHackDbContext db, TimeProvider clock) : INotificationSink, INotificationReader
{
    public void Enqueue(string ownerId, CaseId caseId, string type, string title, string body, string? integrationMode, long sourceEventId) =>
        db.Notifications.Add(new NotificationEntity
        {
            OwnerId = ownerId,
            CaseId = caseId.Value,
            Type = type,
            OccurredAt = clock.GetUtcNow(),
            Title = title,
            Body = body,
            IntegrationMode = integrationMode,
            SourceEventId = sourceEventId,
        });

    public async Task<IReadOnlyList<NotificationEntry>> ListAsync(string ownerId, long after, bool unreadOnly, CancellationToken ct)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.OwnerId == ownerId && n.Id > after);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var rows = await query.OrderBy(n => n.Id).ToListAsync(ct);

        return [.. rows.Select(n => new NotificationEntry(
            n.Id, new CaseId(n.CaseId), n.Type, n.OccurredAt, n.ReadAt, n.Title, n.Body, n.IntegrationMode))];
    }

    public async Task AckAsync(string ownerId, IReadOnlyList<long> notificationIds, CancellationToken ct)
    {
        var rows = await db.Notifications
            .Where(n => n.OwnerId == ownerId && notificationIds.Contains(n.Id) && n.ReadAt == null)
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        foreach (var row in rows)
        {
            row.ReadAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
