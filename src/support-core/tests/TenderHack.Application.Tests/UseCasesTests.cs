using TenderHack.Application.Exceptions;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Tests.Fakes;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Cases;
using Xunit;

namespace TenderHack.Application.Tests;

public sealed class UseCasesTests
{
    [Fact]
    public async Task CreateCasePersistsAndReturnsANewCaseForTheOwner()
    {
        var repository = new FakeCaseRepository();
        var unitOfWork = new FakeUnitOfWork();
        var sut = new CreateCaseUseCase(repository, unitOfWork);

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
        var orchestrator = new TurnOrchestrator(
            new FakeKnowledgeService(), new FakeModerationRuleEngine(), new FakeTurnEventStream(),
            new ModerationOptions(), TimeProvider.System);
        var @case = new Case(CaseId.New(), "owner-1");
        repository.Add(@case);
        var sut = new SendMessageUseCase(repository, unitOfWork, orchestrator);

        var outcome = await sut.ExecuteAsync(@case.Id, "owner-1", "вопрос", CancellationToken.None);

        Assert.Equal(Decision.Answer, outcome.Decision);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task SendMessageToUnknownCaseThrowsNotFound()
    {
        var repository = new FakeCaseRepository();
        var orchestrator = new TurnOrchestrator(
            new FakeKnowledgeService(), new FakeModerationRuleEngine(), new FakeTurnEventStream(),
            new ModerationOptions(), TimeProvider.System);
        var sut = new SendMessageUseCase(repository, new FakeUnitOfWork(), orchestrator);

        await Assert.ThrowsAsync<CaseNotFoundException>(() =>
            sut.ExecuteAsync(CaseId.New(), "owner-1", "вопрос", CancellationToken.None));
    }

    [Fact]
    public async Task SendMessageFromANonOwnerIsTreatedAsNotFound()
    {
        var repository = new FakeCaseRepository();
        var @case = new Case(CaseId.New(), "owner-1");
        repository.Add(@case);
        var orchestrator = new TurnOrchestrator(
            new FakeKnowledgeService(), new FakeModerationRuleEngine(), new FakeTurnEventStream(),
            new ModerationOptions(), TimeProvider.System);
        var sut = new SendMessageUseCase(repository, new FakeUnitOfWork(), orchestrator);

        await Assert.ThrowsAsync<CaseNotFoundException>(() =>
            sut.ExecuteAsync(@case.Id, "someone-else", "вопрос", CancellationToken.None));
    }

    [Fact]
    public async Task GetCaseSnapshotReturnsTheCaseForItsOwner()
    {
        var repository = new FakeCaseRepository();
        var @case = new Case(CaseId.New(), "owner-1");
        repository.Add(@case);
        var sut = new GetCaseSnapshotUseCase(repository);

        var result = await sut.ExecuteAsync(@case.Id, "owner-1", CancellationToken.None);

        Assert.Same(@case, result);
    }

    [Fact]
    public async Task GetCaseSnapshotFromANonOwnerIsTreatedAsNotFound()
    {
        var repository = new FakeCaseRepository();
        var @case = new Case(CaseId.New(), "owner-1");
        repository.Add(@case);
        var sut = new GetCaseSnapshotUseCase(repository);

        await Assert.ThrowsAsync<CaseNotFoundException>(() =>
            sut.ExecuteAsync(@case.Id, "someone-else", CancellationToken.None));
    }
}
