using TenderHack.Domain.Cases;

namespace TenderHack.Application.Ports;

/// <summary>Read side of the `case_events` log `ITurnEventStream` writes — event catch-up and SSE (web-api-v0.md §3, §6).</summary>
public interface ICaseEventReader
{
    /// <summary>Events with `EventId &gt; after`, ordered by `EventId` ascending.</summary>
    Task<IReadOnlyList<PersistedCaseEvent>> ListAsync(CaseId caseId, long after, CancellationToken ct);
}

public sealed record PersistedCaseEvent(
    long EventId,
    TurnId? TurnId,
    int? Revision,
    string Type,
    DateTimeOffset OccurredAt,
    string PayloadJson);
