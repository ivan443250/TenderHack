namespace TenderHack.Infrastructure.Persistence;

/// <summary>Persistence row for `case_events` (architecture.md §5.2). Written one row per stage.</summary>
public sealed class CaseEventEntity
{
    public long Id { get; init; }

    public required Guid CaseId { get; init; }

    public Guid? TurnId { get; init; }

    public int? Revision { get; init; }

    public required string Type { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public required string PayloadJson { get; init; }
}
