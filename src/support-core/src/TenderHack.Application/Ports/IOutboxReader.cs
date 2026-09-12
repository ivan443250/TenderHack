namespace TenderHack.Application.Ports;

/// <summary>
/// Read/ack side of the `outbox` table `IOutbox` writes to — `api-worker`'s delivery loop
/// (architecture.md §12). <see cref="MarkDeliveredAsync"/>/<see cref="MarkFailedAsync"/> stage the
/// mutation on the same unit of work as everything else the caller changed this tick; call
/// <see cref="IUnitOfWork.SaveChangesAsync"/> once to commit both atomically.
/// </summary>
public interface IOutboxReader
{
    Task<IReadOnlyList<OutboxEntry>> ListPendingAsync(string messageType, int batchSize, CancellationToken ct);

    Task MarkDeliveredAsync(long id, CancellationToken ct);

    Task MarkFailedAsync(long id, string error, CancellationToken ct);
}

public sealed record OutboxEntry(long Id, string MessageType, string PayloadJson, int AttemptCount);
