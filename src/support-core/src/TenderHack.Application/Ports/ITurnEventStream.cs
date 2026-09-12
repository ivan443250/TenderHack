using TenderHack.Domain.Cases;

namespace TenderHack.Application.Ports;

/// <summary>
/// Publishes one `case_events` row (architecture.md §5.2: persisted before the next stage starts)
/// and fans it out over SSE. Persistence and live push are one seam because they are one write in
/// the same transaction from the caller's point of view; Infrastructure may still implement them as
/// two internal steps.
/// </summary>
public interface ITurnEventStream
{
    Task PublishAsync(CaseId caseId, CaseEvent @event, CancellationToken ct);
}

/// <summary>
/// Application-owned event shape. <c>Payload</c> is a plain data bag — the exact wire JSON
/// (web-api-v0.md §4.2 `TimelineItem`) is assembled by `TenderHack.Api`, not here.
/// </summary>
public sealed record CaseEvent(
    string Type,
    TurnId? TurnId,
    int? Revision,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, object?> Payload);
