namespace TenderHack.Infrastructure.Persistence;

/// <summary>Persistence row for `idempotency_keys` (architecture.md §8).</summary>
public sealed class IdempotencyKeyEntity
{
    public long Id { get; init; }

    public required string OwnerId { get; init; }

    public required string Scope { get; init; }

    public required string Key { get; init; }

    public required string PayloadHash { get; init; }

    public required string EntityId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
