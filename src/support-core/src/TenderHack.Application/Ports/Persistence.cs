using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Application.Ports;

/// <summary>
/// One repository for the one aggregate root (`Case`) — not a repository-per-table. Loads/attaches
/// for EF Core change tracking; it never exposes query composition into Application.
/// </summary>
public interface ICaseRepository
{
    Task<Case?> FindAsync(CaseId caseId, CancellationToken ct);

    Task<IReadOnlyList<Case>> ListByOwnerAsync(string ownerId, CancellationToken ct);

    /// <summary>Cases whose handoff is accepted, not yet terminal, and not yet marked stale — `handoff-status-sync`'s worklist.</summary>
    Task<IReadOnlyList<Case>> ListPendingHandoffPollsAsync(CancellationToken ct);

    /// <summary>Looks a case up by its handoff id — the inbound status webhook only carries `handoff_id`, not `case_id`.</summary>
    Task<Case?> FindByHandoffIdAsync(HandoffId handoffId, CancellationToken ct);

    /// <summary>
    /// Cases whose active turn has been stuck `Queued`/`Running` since before <paramref name="olderThan"/> —
    /// almost always a process crash mid-turn. Only one turn per case can be in that state at a time
    /// (starting a new one always supersedes the previous), so matching any such turn is safe.
    /// </summary>
    Task<IReadOnlyList<Case>> ListStaleActiveTurnCasesAsync(DateTimeOffset olderThan, CancellationToken ct);

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
