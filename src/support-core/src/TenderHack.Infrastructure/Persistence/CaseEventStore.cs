using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Infrastructure.Persistence;

/// <summary>
/// Writes each stage's `case_events` row through its own short-lived <see cref="TenderHackDbContext"/>
/// (via <see cref="IDbContextFactory{TenderHackDbContext}"/>), independent of the ambient
/// unit-of-work for the `Case` aggregate — architecture.md §5.2 requires the row durable before the
/// next stage starts, which the aggregate's own single end-of-turn commit cannot guarantee.
/// </summary>
public sealed class CaseEventStore(IDbContextFactory<TenderHackDbContext> dbContextFactory) : ITurnEventStream
{
    public async Task PublishAsync(CaseId caseId, CaseEvent @event, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        db.CaseEvents.Add(new CaseEventEntity
        {
            CaseId = caseId.Value,
            TurnId = @event.TurnId?.Value,
            Revision = @event.Revision,
            Type = @event.Type,
            OccurredAt = @event.OccurredAt,
            PayloadJson = JsonSerializer.Serialize(@event.Payload),
        });

        await db.SaveChangesAsync(ct);
    }
}
