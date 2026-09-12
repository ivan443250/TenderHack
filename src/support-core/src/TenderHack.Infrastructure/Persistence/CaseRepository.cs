using Microsoft.EntityFrameworkCore;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Infrastructure.Persistence;

public sealed class CaseRepository(TenderHackDbContext db) : ICaseRepository
{
    public Task<Case?> FindAsync(CaseId caseId, CancellationToken ct) =>
        db.Cases.FirstOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<Case>> ListByOwnerAsync(string ownerId, CancellationToken ct) =>
        await db.Cases.Where(c => c.OwnerId == ownerId).ToListAsync(ct);

    public async Task<IReadOnlyList<Case>> ListPendingHandoffPollsAsync(CancellationToken ct) =>
        await db.Cases
            .Where(c => c.Handoff != null
                && (c.Handoff.Status == HandoffStatus.Accepted || c.Handoff.Status == HandoffStatus.SimulatedAccepted)
                && c.Handoff.Terminal == null
                && !c.Handoff.Stale)
            .ToListAsync(ct);

    public Task<Case?> FindByHandoffIdAsync(HandoffId handoffId, CancellationToken ct) =>
        db.Cases.FirstOrDefaultAsync(c => c.Handoff != null && c.Handoff.Id == handoffId, ct);

    public async Task<IReadOnlyList<Case>> ListStaleActiveTurnCasesAsync(DateTimeOffset olderThan, CancellationToken ct) =>
        await db.Cases
            .Where(c => c.Turns.Any(t =>
                (t.Status == TurnStatus.Queued || t.Status == TurnStatus.Running) && t.CreatedAt < olderThan))
            .ToListAsync(ct);

    public void Add(Case @case) => db.Cases.Add(@case);
}

public sealed class UnitOfWork(TenderHackDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
