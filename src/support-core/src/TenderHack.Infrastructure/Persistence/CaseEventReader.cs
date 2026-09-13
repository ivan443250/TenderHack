using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Infrastructure.Persistence;

public sealed class CaseEventReader(TenderHackDbContext db) : ICaseEventReader
{
    public async Task<IReadOnlyList<PersistedCaseEvent>> ListAsync(CaseId caseId, long after, CancellationToken ct)
    {
        var rows = await db.CaseEvents
            .AsNoTracking()
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

    public async Task<IReadOnlyDictionary<CaseId, string>> ListFirstUserMessageTextsAsync(
        IReadOnlyCollection<CaseId> caseIds,
        CancellationToken ct)
    {
        if (caseIds.Count == 0)
        {
            return new Dictionary<CaseId, string>();
        }

        var values = caseIds.Select(id => id.Value).ToArray();
        var rows = await db.CaseEvents
            .AsNoTracking()
            .Where(e => values.Contains(e.CaseId) && e.Type == "USER_MESSAGE")
            .OrderBy(e => e.Id)
            .Select(e => new { e.CaseId, e.PayloadJson })
            .ToListAsync(ct);

        var result = new Dictionary<CaseId, string>();
        foreach (var row in rows)
        {
            var caseId = new CaseId(row.CaseId);
            if (result.ContainsKey(caseId))
            {
                continue;
            }

            try
            {
                using var payload = JsonDocument.Parse(row.PayloadJson);
                if (payload.RootElement.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String
                    && text.GetString() is { } value)
                {
                    result[caseId] = value;
                }
            }
            catch (JsonException)
            {
                // A malformed historical event cannot break the case list; that case gets the fallback title.
            }
        }

        return result;
    }
}
