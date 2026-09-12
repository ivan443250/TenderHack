using TenderHack.Application.Tests.Fakes;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Cases;
using Xunit;

namespace TenderHack.Application.Tests;

public sealed class CleanUpStaleTurnsUseCaseTests
{
    private static Case NewCase(string ownerId = "owner-1") => new(CaseId.New(), ownerId, DateTimeOffset.UtcNow);

    [Fact]
    public async Task FailsATurnStuckPastTheTtlAndPublishesTechnicalError()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        var longAgo = DateTimeOffset.UtcNow.AddMinutes(-10);
        @case.StartTurn(longAgo); // never completed — simulates a crash mid-turn
        repository.Add(@case);
        var events = new FakeTurnEventStream();
        var unitOfWork = new FakeUnitOfWork();
        var sut = new CleanUpStaleTurnsUseCase(repository, unitOfWork, events, TimeProvider.System);

        var cleaned = await sut.ExecuteAsync(TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(1, cleaned);
        Assert.Equal(TurnStatus.Failed, @case.ActiveTurn!.Status);
        Assert.Equal(Decision.TechnicalError, @case.ActiveTurn.Decision);
        Assert.Contains(events.Published, e => e.Event.Type == "TECHNICAL_ERROR");
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task DoesNotTouchATurnStillWithinTheTtl()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.StartTurn(DateTimeOffset.UtcNow); // fresh, well within TTL
        repository.Add(@case);
        var events = new FakeTurnEventStream();
        var sut = new CleanUpStaleTurnsUseCase(repository, new FakeUnitOfWork(), events, TimeProvider.System);

        var cleaned = await sut.ExecuteAsync(TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(0, cleaned);
        Assert.Equal(TurnStatus.Queued, @case.ActiveTurn!.Status);
        Assert.Empty(events.Published);
    }

    [Fact]
    public async Task DoesNotTouchACompletedTurnEvenIfOld()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        var turn = @case.StartTurn(DateTimeOffset.UtcNow.AddMinutes(-10));
        @case.TryPublishDecision(turn.Id, turn.Revision, Decision.Answer);
        repository.Add(@case);
        var sut = new CleanUpStaleTurnsUseCase(repository, new FakeUnitOfWork(), new FakeTurnEventStream(), TimeProvider.System);

        var cleaned = await sut.ExecuteAsync(TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(0, cleaned);
        Assert.Equal(TurnStatus.Completed, @case.ActiveTurn!.Status);
    }
}
