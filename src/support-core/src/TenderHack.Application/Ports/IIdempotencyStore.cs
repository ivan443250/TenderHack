namespace TenderHack.Application.Ports;

/// <summary>
/// Generic idempotency-key ledger (architecture.md §7, §8 `idempotency_keys`): same key + same
/// payload → same logical result; same key + different payload → conflict
/// (<see cref="TenderHack.Application.Exceptions.IdempotencyConflictException"/>, mapped to 409).
/// `EntityId` is whatever the caller needs to re-fetch the original result — a case id, a feedback
/// id, and so on.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(string ownerId, string scope, string idempotencyKey, CancellationToken ct);

    Task SaveAsync(string ownerId, string scope, string idempotencyKey, string payloadHash, string entityId, CancellationToken ct);
}

public sealed record IdempotencyRecord(string PayloadHash, string EntityId);
