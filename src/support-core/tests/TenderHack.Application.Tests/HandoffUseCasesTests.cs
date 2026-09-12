using TenderHack.Application.Exceptions;
using TenderHack.Application.Handoff;
using TenderHack.Application.Tests.Fakes;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;
using Xunit;

namespace TenderHack.Application.Tests;

public sealed class HandoffUseCasesTests
{
    private static Case NewCase(string ownerId = "owner-1") => new(CaseId.New(), ownerId, DateTimeOffset.UtcNow);

    [Fact]
    public async Task PrepareCreatesAHandoffWithoutSubmittingIt()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        repository.Add(@case);
        var sut = new PrepareHandoffUseCase(repository, new FakeUnitOfWork());

        var result = await sut.ExecuteAsync(@case.Id, "owner-1", CancellationToken.None);

        Assert.NotNull(result.Handoff);
        Assert.Equal(HandoffStatus.NotRequested, result.Handoff!.Status);
    }

    [Fact]
    public async Task PrepareFromANonOwnerIsTreatedAsNotFound()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        repository.Add(@case);
        var sut = new PrepareHandoffUseCase(repository, new FakeUnitOfWork());

        await Assert.ThrowsAsync<CaseNotFoundException>(() => sut.ExecuteAsync(@case.Id, "someone-else", CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmSetsPendingAndEnqueuesTheSubmitMessageAtomically()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.PrepareHandoff();
        repository.Add(@case);
        var outbox = new FakeOutbox();
        var unitOfWork = new FakeUnitOfWork();
        var sut = new ConfirmHandoffUseCase(repository, unitOfWork, outbox);

        var result = await sut.ExecuteAsync(@case.Id, "owner-1", "Резюме обращения", "l2-general", ["INSUFFICIENT_EVIDENCE"], false, CancellationToken.None);

        Assert.Equal(HandoffStatus.Pending, result.Handoff!.Status);
        Assert.Single(outbox.Enqueued);
        Assert.Equal(HandoffOutboxMessages.Submit, outbox.Enqueued[0].MessageType);
        var payload = Assert.IsType<HandoffSubmitPayload>(outbox.Enqueued[0].Payload);
        Assert.Equal("Резюме обращения", payload.Summary);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task RetryReusesTheSameHandoffIdAsIdempotencyKey()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.PrepareHandoff();
        var originalHandoffId = @case.Handoff!.Id;
        @case.ConfirmHandoff();
        @case.FailHandoff();
        repository.Add(@case);
        var outbox = new FakeOutbox();
        var sut = new RetryHandoffUseCase(repository, new FakeUnitOfWork(), outbox);

        var result = await sut.ExecuteAsync(@case.Id, "owner-1", "Резюме", "l1-general", [], false, CancellationToken.None);

        Assert.Equal(HandoffStatus.Pending, result.Handoff!.Status);
        var payload = Assert.IsType<HandoffSubmitPayload>(outbox.Enqueued[0].Payload);
        Assert.Equal(originalHandoffId.ToString(), payload.HandoffId);
    }

    [Fact]
    public async Task IngestHandoffStatusAppliesFactsThroughTheCase()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.PrepareHandoff();
        @case.ConfirmHandoff();
        @case.AcknowledgeHandoff(simulated: true, "ext-1", DateTimeOffset.UtcNow);
        repository.Add(@case);
        var unitOfWork = new FakeUnitOfWork();
        var sut = new IngestHandoffStatusUseCase(repository, unitOfWork, TimeProvider.System);

        await sut.ExecuteAsync(
            @case.Id, @case.Handoff!.Id, externalRevision: 1,
            new HandoffStage("IN_PROGRESS", "В работе"), null, null, CancellationToken.None);

        Assert.Equal("IN_PROGRESS", @case.Handoff.Stage!.Code);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }
}
