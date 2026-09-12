using TenderHack.Application.Exceptions;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Tests.Fakes;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Cases;
using Xunit;

namespace TenderHack.Application.Tests;

public sealed class UseCasesTests
{
    private static TurnOrchestrator NewOrchestrator(FakeUnitOfWork unitOfWork) =>
        new(new FakeKnowledgeService(), new FakeModerationRuleEngine(), new FakeTurnEventStream(), new FakeOutbox(),
            unitOfWork, new ModerationOptions(), TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TurnOrchestrator>.Instance);

    [Fact]
    public async Task CreateCasePersistsAndReturnsANewCaseForTheOwner()
    {
        var repository = new FakeCaseRepository();
        var unitOfWork = new FakeUnitOfWork();
        var sut = new CreateCaseUseCase(repository, unitOfWork, TimeProvider.System);

        var @case = await sut.ExecuteAsync("owner-1", CancellationToken.None);

        Assert.Equal("owner-1", @case.OwnerId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Same(@case, await repository.FindAsync(@case.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SendMessageRunsTheOrchestratorAndPersists()
    {
        var repository = new FakeCaseRepository();
        var unitOfWork = new FakeUnitOfWork();
        var @case = new Case(CaseId.New(), "owner-1", DateTimeOffset.UtcNow);
        repository.Add(@case);
        var sut = new SendMessageUseCase(repository, unitOfWork, NewOrchestrator(unitOfWork));

        var outcome = await sut.ExecuteAsync(@case.Id, "owner-1", "вопрос", CancellationToken.None);

        Assert.Equal(Decision.Answer, outcome.Decision);
        // Two commits per turn: the orchestrator persists the QUEUED turn before the first stage,
        // the use-case persists the final decision.
        Assert.Equal(2, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task SendMessageToUnknownCaseThrowsNotFound()
    {
        var repository = new FakeCaseRepository();
        var unitOfWork = new FakeUnitOfWork();
        var sut = new SendMessageUseCase(repository, unitOfWork, NewOrchestrator(unitOfWork));

        await Assert.ThrowsAsync<CaseNotFoundException>(() =>
            sut.ExecuteAsync(CaseId.New(), "owner-1", "вопрос", CancellationToken.None));
    }

    [Fact]
    public async Task SendMessageFromANonOwnerIsTreatedAsNotFound()
    {
        var repository = new FakeCaseRepository();
        var @case = new Case(CaseId.New(), "owner-1", DateTimeOffset.UtcNow);
        repository.Add(@case);
        var unitOfWork = new FakeUnitOfWork();
        var sut = new SendMessageUseCase(repository, unitOfWork, NewOrchestrator(unitOfWork));

        await Assert.ThrowsAsync<CaseNotFoundException>(() =>
            sut.ExecuteAsync(@case.Id, "someone-else", "вопрос", CancellationToken.None));
    }

    [Fact]
    public async Task GetCaseSnapshotReturnsTheCaseForItsOwner()
    {
        var repository = new FakeCaseRepository();
        var @case = new Case(CaseId.New(), "owner-1", DateTimeOffset.UtcNow);
        repository.Add(@case);
        var sut = new GetCaseSnapshotUseCase(repository);

        var result = await sut.ExecuteAsync(@case.Id, "owner-1", CancellationToken.None);

        Assert.Same(@case, result);
    }

    [Fact]
    public async Task GetCaseSnapshotFromANonOwnerIsTreatedAsNotFound()
    {
        var repository = new FakeCaseRepository();
        var @case = new Case(CaseId.New(), "owner-1", DateTimeOffset.UtcNow);
        repository.Add(@case);
        var sut = new GetCaseSnapshotUseCase(repository);

        await Assert.ThrowsAsync<CaseNotFoundException>(() =>
            sut.ExecuteAsync(@case.Id, "someone-else", CancellationToken.None));
    }
}
