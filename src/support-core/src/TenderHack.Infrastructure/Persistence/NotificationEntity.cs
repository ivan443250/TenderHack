namespace TenderHack.Infrastructure.Persistence;

/// <summary>Persistence row for `notifications` (architecture.md §5.9) — owner-scoped, independent of any one case.</summary>
public sealed class NotificationEntity
{
    public long Id { get; init; }

    public required string OwnerId { get; init; }

    public required Guid CaseId { get; init; }

    public required string Type { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? ReadAt { get; set; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public string? IntegrationMode { get; init; }

    /// <summary>The `case_events` row that caused this notification — uniqueness key together with `(OwnerId, CaseId, Type)` (architecture.md §7).</summary>
    public required long SourceEventId { get; init; }
}
