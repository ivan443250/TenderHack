using Microsoft.EntityFrameworkCore;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Infrastructure.Persistence;

public sealed class CaseEventReader(TenderHackDbContext db) : ICaseEventReader
{
    public async Task<IReadOnlyList<PersistedCaseEvent>> ListAsync(CaseId caseId, long after, CancellationToken ct)
    {
        var rows = await db.CaseEvents
            .Where(e => e.CaseId == caseId.Value && e.Id > after)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

        return [.. rows.Select(e => new PersistedCaseEvent(
            e.Id,
            e.TurnId is { } turnId ? new TurnId(turnId) : null,
            e.Revision,
            e.Type,
            e.OccurredAt,
            e.PayloadJson))];
    }
}
