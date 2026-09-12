using Microsoft.EntityFrameworkCore;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Infrastructure.Persistence;

public sealed class CaseRepository(TenderHackDbContext db) : ICaseRepository
{
    public Task<Case?> FindAsync(CaseId caseId, CancellationToken ct) =>
        db.Cases.FirstOrDefaultAsync(c => c.Id == caseId, ct);

    public void Add(Case @case) => db.Cases.Add(@case);
}

public sealed class UnitOfWork(TenderHackDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
