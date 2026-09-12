using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.Tests.Fakes;

public sealed class FakeCaseRepository : ICaseRepository
{
    private readonly Dictionary<CaseId, Case> _cases = [];

    public Task<Case?> FindAsync(CaseId caseId, CancellationToken ct) =>
        Task.FromResult(_cases.TryGetValue(caseId, out var found) ? found : null);

    public void Add(Case @case) => _cases[@case.Id] = @case;
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}
