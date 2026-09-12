using System.Text.Json;
using TenderHack.Application.Ports;

namespace TenderHack.Infrastructure.Persistence;

/// <summary>
/// Enqueues into the same <see cref="TenderHackDbContext"/> the caller uses, so the outbox row
/// commits atomically with the business state that produced it (architecture.md §5.9, §10) —
/// delivery itself is `api-worker`'s job, not this class's.
/// </summary>
public sealed class Outbox(TenderHackDbContext db, TimeProvider clock) : IOutbox
{
    public void Enqueue(string messageType, object payload)
    {
        db.OutboxMessages.Add(new OutboxMessageEntity
        {
            MessageType = messageType,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = clock.GetUtcNow(),
        });
    }
}
