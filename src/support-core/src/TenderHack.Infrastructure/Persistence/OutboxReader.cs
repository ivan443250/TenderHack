using Microsoft.EntityFrameworkCore;
using TenderHack.Application.Ports;

namespace TenderHack.Infrastructure.Persistence;

public sealed class OutboxReader(TenderHackDbContext db) : IOutboxReader
{
    public async Task<IReadOnlyList<OutboxEntry>> ListPendingAsync(string messageType, int batchSize, CancellationToken ct)
    {
        var rows = await db.OutboxMessages
            .Where(m => m.MessageType == messageType && m.DeliveredAt == null)
            .OrderBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        return [.. rows.Select(m => new OutboxEntry(m.Id, m.MessageType, m.PayloadJson, m.AttemptCount))];
    }

    /// <summary>
    /// Stages the mutation on the ambient scoped `DbContext`; it commits together with whatever
    /// domain state the caller changed in the same tick, via the caller's own <see cref="IUnitOfWork.SaveChangesAsync"/> —
    /// one atomic commit per outbox row processed, not two.
    /// </summary>
    public async Task MarkDeliveredAsync(long id, CancellationToken ct)
    {
        var row = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null)
        {
            return;
        }

        row.DeliveredAt = DateTimeOffset.UtcNow;
    }

    public async Task MarkFailedAsync(long id, string error, CancellationToken ct)
    {
        var row = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null)
        {
            return;
        }

        row.AttemptCount++;
        row.LastError = error;
        // A failed submit attempt still terminates this outbox row (architecture.md §15: retry is a
        // fresh user-triggered `/handoff/retry` command, not the worker re-driving the same row).
        row.DeliveredAt = DateTimeOffset.UtcNow;
    }

    public async Task RecordAttemptFailureAsync(long id, string error, CancellationToken ct)
    {
        var row = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null)
        {
            return;
        }

        row.AttemptCount++;
        row.LastError = error;
        // Left pending (no DeliveredAt) — at-least-once, the next tick retries it.
    }
}
