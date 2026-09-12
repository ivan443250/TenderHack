using Microsoft.EntityFrameworkCore;
using TenderHack.Application.Ports;

namespace TenderHack.Infrastructure.Persistence;

/// <summary>
/// Self-contained: <see cref="SaveAsync"/> commits immediately rather than riding the caller's own
/// unit of work, because it runs after the caller's use-case has already committed its business
/// effect — this is a best-effort record of "that already happened", not part of the same
/// transaction (architecture.md §7: "no exactly-once claim").
/// </summary>
public sealed class IdempotencyStore(TenderHackDbContext db, TimeProvider clock) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> FindAsync(string ownerId, string scope, string idempotencyKey, CancellationToken ct)
    {
        var row = await db.IdempotencyKeys.FirstOrDefaultAsync(
            k => k.OwnerId == ownerId && k.Scope == scope && k.Key == idempotencyKey, ct);

        return row is null ? null : new IdempotencyRecord(row.PayloadHash, row.EntityId);
    }

    public async Task SaveAsync(string ownerId, string scope, string idempotencyKey, string payloadHash, string entityId, CancellationToken ct)
    {
        var entry = db.IdempotencyKeys.Add(new IdempotencyKeyEntity
        {
            OwnerId = ownerId,
            Scope = scope,
            Key = idempotencyKey,
            PayloadHash = payloadHash,
            EntityId = entityId,
            CreatedAt = clock.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent request recorded the same (owner, scope, key) first — its result stands.
            // Detach so this failed insert does not poison a later SaveChangesAsync on the same
            // ambient scoped context.
            entry.State = EntityState.Detached;
        }
    }
}
