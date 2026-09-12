using TenderHack.Application.Exceptions;
using TenderHack.Application.Handoff;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Tests.Fakes;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;
using Xunit;

namespace TenderHack.Application.Tests;

public sealed class HandoffUseCasesTests
{
    private static Case NewCase(string ownerId = "owner-1") => new(CaseId.New(), ownerId, DateTimeOffset.UtcNow);

    private static HandoffPackage NewPackage() => new(
        DraftSummary: "Резюме обращения",
        UserReportedContext: [],
        VerifiedPortalContext: [],
        AlreadyTried: [],
        UnknownFields: [],
        SourcesChecked: [],
        HandoffReason: ["INSUFFICIENT_EVIDENCE"],
        RelevantMessageIds: [],
        Channel: HandoffPackage.PortalChatChannel,
        DispatchQueue: "l2-general",
        EngineeringReviewSuggested: false);

    [Fact]
    public async Task PrepareCreatesAHandoffWithoutSubmittingIt()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        repository.Add(@case);
        var sut = new PrepareHandoffUseCase(repository, new FakeCaseEventReader(), new FakeUnitOfWork());

        var result = await sut.ExecuteAsync(@case.Id, "owner-1", CancellationToken.None);

        Assert.NotNull(result.Handoff);
        Assert.Equal(HandoffStatus.NotRequested, result.Handoff!.Status);
        Assert.NotNull(result.Handoff.Package);
    }

    [Fact]
    public async Task PrepareAssemblesThePackageFromPersistedCaseHistoryAlone()
    {
        // support-adapter-v0.md §2: the package must be buildable without any knowledge call —
        // this test only ever touches ICaseRepository/ICaseEventReader.
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.ObserveUnderstanding("вопрос", [new ContextSlot("role", "поставщик", ContextSlotProvenance.UserExplicit)]);
        repository.Add(@case);
        var eventReader = new FakeCaseEventReader();
        eventReader.Add("USER_MESSAGE", """{"text":"как исправить УПД?"}""");
        eventReader.Add("HANDOFF_OFFER", """{"dispatch_queue":"l2-general","reason_codes":["INSUFFICIENT_EVIDENCE"],"engineering_review_suggested":false}""");
        var sut = new PrepareHandoffUseCase(repository, eventReader, new FakeUnitOfWork());

        var result = await sut.ExecuteAsync(@case.Id, "owner-1", CancellationToken.None);

        var package = result.Handoff!.Package!;
        Assert.Equal("l2-general", package.DispatchQueue);
        Assert.Equal(["INSUFFICIENT_EVIDENCE"], package.HandoffReason);
        Assert.Contains(package.UserReportedContext, s => s.Type == "role" && s.Value == "поставщик");
        Assert.Contains("как исправить УПД?", package.DraftSummary);
        Assert.Equal(HandoffPackage.PortalChatChannel, package.Channel);
    }

    [Fact]
    public async Task PrepareFromANonOwnerIsTreatedAsNotFound()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        repository.Add(@case);
        var sut = new PrepareHandoffUseCase(repository, new FakeCaseEventReader(), new FakeUnitOfWork());

        await Assert.ThrowsAsync<CaseNotFoundException>(() => sut.ExecuteAsync(@case.Id, "someone-else", CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmSetsPendingAndEnqueuesTheSubmitMessageAtomically()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.PrepareHandoff(NewPackage());
        repository.Add(@case);
        var outbox = new FakeOutbox();
        var unitOfWork = new FakeUnitOfWork();
        var sut = new ConfirmHandoffUseCase(repository, unitOfWork, outbox);

        var result = await sut.ExecuteAsync(@case.Id, "owner-1", "Резюме обращения", CancellationToken.None);

        Assert.Equal(HandoffStatus.Pending, result.Handoff!.Status);
        Assert.Equal("Резюме обращения", result.Handoff.ConfirmedSummary);
        Assert.Single(outbox.Enqueued);
        Assert.Equal(HandoffOutboxMessages.Submit, outbox.Enqueued[0].MessageType);
        var payload = Assert.IsType<HandoffSubmitPayload>(outbox.Enqueued[0].Payload);
        Assert.Equal(@case.Handoff!.Id.ToString(), payload.HandoffId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task RetryReusesTheSameHandoffIdAsIdempotencyKey()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.PrepareHandoff(NewPackage());
        var originalHandoffId = @case.Handoff!.Id;
        @case.ConfirmHandoff("Резюме");
        @case.FailHandoff();
        repository.Add(@case);
        var outbox = new FakeOutbox();
        var sut = new RetryHandoffUseCase(repository, new FakeUnitOfWork(), outbox);

        var result = await sut.ExecuteAsync(@case.Id, "owner-1", "Обновлённое резюме", CancellationToken.None);

        Assert.Equal(HandoffStatus.Pending, result.Handoff!.Status);
        Assert.Equal("Обновлённое резюме", result.Handoff.ConfirmedSummary);
        var payload = Assert.IsType<HandoffSubmitPayload>(outbox.Enqueued[0].Payload);
        Assert.Equal(originalHandoffId.ToString(), payload.HandoffId);
    }

    private static IngestHandoffStatusUseCase CreateIngestUseCase(
        FakeCaseRepository repository, FakeUnitOfWork unitOfWork, FakeTurnEventStream events, FakeNotificationSink notifications) =>
        new(repository, unitOfWork, events, notifications, new CaseCompletionPublisher(events, notifications, new FakeOutbox()), TimeProvider.System);

    [Fact]
    public async Task IngestHandoffStatusAppliesFactsThroughTheCase()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.PrepareHandoff(NewPackage());
        @case.ConfirmHandoff("Резюме");
        @case.AcknowledgeHandoff(simulated: true, "ext-1", DateTimeOffset.UtcNow);
        repository.Add(@case);
        var unitOfWork = new FakeUnitOfWork();
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var sut = CreateIngestUseCase(repository, unitOfWork, events, notifications);

        await sut.ExecuteAsync(
            @case.Id, @case.Handoff!.Id, externalRevision: 1,
            new HandoffStage("IN_PROGRESS", "В работе"), null, null, CancellationToken.None);

        Assert.Equal("IN_PROGRESS", @case.Handoff.Stage!.Code);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        var published = Assert.Single(events.Published, e => e.Event.Type == "HANDOFF_STATUS");
        // web-api-v0.md §4.2: the payload is the full current handoff view, not just this update's delta.
        Assert.Equal("SimulatedAccepted", published.Event.Payload["status"]);
        Assert.Equal("ext-1", published.Event.Payload["external_case_id"]);
        Assert.False((bool)published.Event.Payload["stale"]!);
        Assert.Equal(new[] { "stage" }, published.Event.Payload["changed"]);
        Assert.Contains(notifications.Enqueued, n => n.Type == "HANDOFF_UPDATED");
    }

    [Fact]
    public async Task DuplicateRevisionPublishesNothingAndDoesNotSave()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.PrepareHandoff(NewPackage());
        @case.ConfirmHandoff("Резюме");
        @case.AcknowledgeHandoff(simulated: true, "ext-1", DateTimeOffset.UtcNow);
        @case.IngestHandoffStatus(@case.Handoff!.Id, 1, new HandoffStage("QUEUED", null), null, null, DateTimeOffset.UtcNow);
        repository.Add(@case);
        var unitOfWork = new FakeUnitOfWork();
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var sut = CreateIngestUseCase(repository, unitOfWork, events, notifications);

        await sut.ExecuteAsync(@case.Id, @case.Handoff.Id, externalRevision: 1, new HandoffStage("ASSIGNED", null), null, null, CancellationToken.None);

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(events.Published);
        Assert.Empty(notifications.Enqueued);
    }

    [Fact]
    public async Task TerminalFactOnAnActiveCasePublishesCompletionAndFeedbackRequested()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.PrepareHandoff(NewPackage());
        @case.ConfirmHandoff("Резюме");
        @case.AcknowledgeHandoff(simulated: true, "ext-1", DateTimeOffset.UtcNow);
        repository.Add(@case);
        var unitOfWork = new FakeUnitOfWork();
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var sut = CreateIngestUseCase(repository, unitOfWork, events, notifications);

        await sut.ExecuteAsync(@case.Id, @case.Handoff!.Id, externalRevision: 1, null, null, HandoffTerminalOutcome.Resolved, CancellationToken.None);

        Assert.Equal(ConversationStatus.ClosedSupport, @case.ConversationStatus);
        Assert.Equal(ResolutionStatus.Resolved, @case.ResolutionStatus);
        Assert.Contains(events.Published, e => e.Event.Type == "CASE_COMPLETED");
        Assert.Contains(events.Published, e => e.Event.Type == "FEEDBACK_REQUESTED");
        Assert.Contains(notifications.Enqueued, n => n.Type == "CASE_COMPLETED");
    }
}
