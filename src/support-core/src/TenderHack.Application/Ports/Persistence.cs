using TenderHack.Domain.Cases;

namespace TenderHack.Application.Ports;

/// <summary>
/// One repository for the one aggregate root (`Case`) — not a repository-per-table. Loads/attaches
/// for EF Core change tracking; it never exposes query composition into Application.
/// </summary>
public interface ICaseRepository
{
    Task<Case?> FindAsync(CaseId caseId, CancellationToken ct);

    void Add(Case @case);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>
/// Durable at-least-once delivery seam (architecture.md §5.9, §10): a queued message is delivered by
/// `api-worker` after the enclosing transaction commits. Payload shape is the caller's concern —
/// Infrastructure serializes it when persisting to the `outbox` table.
/// </summary>
public interface IOutbox
{
    void Enqueue(string messageType, object payload);
}
