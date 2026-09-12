namespace TenderHack.Infrastructure.Persistence;

/// <summary>Persistence row for `outbox` (architecture.md §5.9, §10) — at-least-once delivery by `api-worker`.</summary>
public sealed class OutboxMessageEntity
{
    public long Id { get; init; }

    public required string MessageType { get; init; }

    public required string PayloadJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }
}
