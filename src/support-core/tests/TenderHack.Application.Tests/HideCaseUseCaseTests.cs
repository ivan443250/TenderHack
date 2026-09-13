using TenderHack.Application.Exceptions;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Tests.Fakes;
using TenderHack.Application.UseCases;
using TenderHack.Domain;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;
using Xunit;

namespace TenderHack.Application.Tests;

public sealed class HideCaseUseCaseTests
{
    private static Case NewCase(string ownerId = "owner-1") => new(CaseId.New(), ownerId, DateTimeOffset.UtcNow);

    private static HandoffPackage NewHandoffPackage() => new(
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

    private static (HideCaseUseCase Sut, FakeTurnEventStream Events, FakeNotificationSink Notifications, FakeCaseRepository Repository) CreateSut()
    {
        var repository = new FakeCaseRepository();
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var publisher = new CaseCompletionPublisher(events, notifications, new FakeOutbox());
        var sut = new HideCaseUseCase(repository, new FakeUnitOfWork(), publisher, TimeProvider.System);
        return (sut, events, notifications, repository);
    }

    [Fact]
    public async Task HidingAnActiveCaseCompletesItWithExactlyOneCaseCompletedAndNoFeedbackOrNotification()
    {
        var (sut, events, notifications, repository) = CreateSut();
        var @case = NewCase();
        repository.Add(@case);

        await sut.ExecuteAsync(@case.Id, "owner-1", CancellationToken.None);

        Assert.NotNull(@case.HiddenAt);
        Assert.Equal(ConversationStatus.ClosedUser, @case.ConversationStatus);
        Assert.Single(events.Published, e => e.Event.Type == "CASE_COMPLETED");
        Assert.DoesNotContain(events.Published, e => e.Event.Type == "FEEDBACK_REQUESTED");
        Assert.DoesNotContain(notifications.Enqueued, n => n.Type is "CASE_COMPLETED" or "FEEDBACK_REQUESTED");
    }

    [Fact]
    public async Task HidingAnAlreadyClosedCaseDoesNotRepublishCompletion()
    {
        var (sut, events, _, repository) = CreateSut();
        var @case = NewCase();
        @case.CompleteByUser(true, DateTimeOffset.UtcNow);
        repository.Add(@case);

        await sut.ExecuteAsync(@case.Id, "owner-1", CancellationToken.None);

        Assert.NotNull(@case.HiddenAt);
        Assert.DoesNotContain(events.Published, e => e.Event.Type == "CASE_COMPLETED");
    }

    [Fact]
    public async Task HidingACaseWithALiveHandoffThrowsAndDoesNotHide()
    {
        var (sut, events, _, repository) = CreateSut();
        var @case = NewCase();
        @case.PrepareHandoff(NewHandoffPackage());
        @case.ConfirmHandoff("Резюме");
        @case.AcknowledgeHandoff(simulated: false, "ext-1", DateTimeOffset.UtcNow);
        repository.Add(@case);

        await Assert.ThrowsAsync<HandoffInProgressException>(() => sut.ExecuteAsync(@case.Id, "owner-1", CancellationToken.None));

        Assert.Null(@case.HiddenAt);
        Assert.Empty(events.Published);
    }

    [Fact]
    public async Task HidingSomeoneElsesCaseThrowsNotFound()
    {
        var (sut, _, _, repository) = CreateSut();
        var @case = NewCase("owner-1");
        repository.Add(@case);

        await Assert.ThrowsAsync<CaseNotFoundException>(() => sut.ExecuteAsync(@case.Id, "owner-2", CancellationToken.None));
    }
}
