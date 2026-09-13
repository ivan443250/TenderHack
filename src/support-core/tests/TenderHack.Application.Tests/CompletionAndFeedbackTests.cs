using TenderHack.Application.Exceptions;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Tests.Fakes;
using TenderHack.Application.UseCases;
using TenderHack.Domain;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Feedback;
using Xunit;

namespace TenderHack.Application.Tests;

public sealed class CompletionAndFeedbackTests
{
    private static Case NewCase(string ownerId = "owner-1") => new(CaseId.New(), ownerId, DateTimeOffset.UtcNow);

    [Fact]
    public async Task CompletingByUserPublishesCompletedAndFeedbackRequestedButNotResolutionChangedWhenUnknown()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        repository.Add(@case);
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var sut = new CompleteCaseUseCase(repository, new FakeUnitOfWork(), new CaseCompletionPublisher(events, notifications, new FakeOutbox()), TimeProvider.System);

        await sut.ExecuteAsync(@case.Id, "owner-1", solved: null, CancellationToken.None);

        Assert.Equal(ConversationStatus.ClosedUser, @case.ConversationStatus);
        Assert.Contains(events.Published, e => e.Event.Type == "CASE_COMPLETED");
        Assert.Contains(events.Published, e => e.Event.Type == "FEEDBACK_REQUESTED");
        Assert.DoesNotContain(events.Published, e => e.Event.Type == "CASE_RESOLUTION_CHANGED");
    }

    [Fact]
    public async Task CompletingByUserWithSolvedTruePublishesResolutionChanged()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        repository.Add(@case);
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var sut = new CompleteCaseUseCase(repository, new FakeUnitOfWork(), new CaseCompletionPublisher(events, notifications, new FakeOutbox()), TimeProvider.System);

        await sut.ExecuteAsync(@case.Id, "owner-1", solved: true, CancellationToken.None);

        Assert.Equal(ResolutionStatus.Resolved, @case.ResolutionStatus);
        Assert.Contains(events.Published, e => e.Event.Type == "CASE_RESOLUTION_CHANGED");
    }

    [Fact]
    public async Task CompletingAnAlreadyCompletedCaseThrowsAndPublishesNothing()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.CompleteByUser(true, DateTimeOffset.UtcNow);
        repository.Add(@case);
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var sut = new CompleteCaseUseCase(repository, new FakeUnitOfWork(), new CaseCompletionPublisher(events, notifications, new FakeOutbox()), TimeProvider.System);

        await Assert.ThrowsAsync<CaseAlreadyCompletedException>(() => sut.ExecuteAsync(@case.Id, "owner-1", false, CancellationToken.None));
        Assert.Empty(events.Published);
    }

    [Fact]
    public async Task ModerationClosedCaseNeverAsksForFeedback()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.StartTurn(DateTimeOffset.UtcNow);
        @case.RecordModerationViolation(closeAfterWarnings: 0, DateTimeOffset.UtcNow); // immediately closes
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var publisher = new CaseCompletionPublisher(events, notifications, new FakeOutbox());

        await publisher.PublishAsync(@case, ResolutionStatus.Unknown, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Contains(events.Published, e => e.Event.Type == "CASE_COMPLETED");
        Assert.DoesNotContain(events.Published, e => e.Event.Type == "FEEDBACK_REQUESTED");
        Assert.DoesNotContain(notifications.Enqueued, n => n.Type == "FEEDBACK_REQUESTED");
    }

    [Fact]
    public async Task SubmitFeedbackRecordsItOnceAndMovesResolutionOnlyFromUnknown()
    {
        var repository = new FakeCaseRepository();
        var feedbackRepository = new FakeFeedbackRepository();
        var @case = NewCase();
        @case.CompleteByUser(null, DateTimeOffset.UtcNow);
        repository.Add(@case);
        var events = new FakeTurnEventStream();
        var sut = new SubmitFeedbackUseCase(repository, feedbackRepository, new FakeUnitOfWork(), events, new FakeOutbox(), TimeProvider.System);

        var feedback = await sut.ExecuteAsync(@case.Id, "owner-1", FeedbackRating.Positive, FeedbackRating.Negative, solved: true, "спасибо", CancellationToken.None);

        Assert.Equal(ResolutionStatus.Resolved, @case.ResolutionStatus);
        Assert.Equal(@case.Id, feedback.CaseId);
        Assert.Same(feedback, Assert.Single(feedbackRepository.Added));
        Assert.Contains(events.Published, e => e.Event.Type == "FEEDBACK_SUBMITTED");
        // The `solved` signal moved resolution, and the timeline shows it like any other transition.
        var changed = Assert.Single(events.Published, e => e.Event.Type == "CASE_RESOLUTION_CHANGED");
        Assert.Equal("RESOLVED", changed.Event.Payload["resolution_status"]);
    }

    [Fact]
    public async Task RatingOnlyFeedbackDoesNotPublishResolutionChanged()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        @case.CompleteByUser(null, DateTimeOffset.UtcNow);
        repository.Add(@case);
        var events = new FakeTurnEventStream();
        var sut = new SubmitFeedbackUseCase(repository, new FakeFeedbackRepository(), new FakeUnitOfWork(), events, new FakeOutbox(), TimeProvider.System);

        await sut.ExecuteAsync(@case.Id, "owner-1", FeedbackRating.Positive, null, solved: null, null, CancellationToken.None);

        Assert.Equal(ResolutionStatus.Unknown, @case.ResolutionStatus);
        Assert.DoesNotContain(events.Published, e => e.Event.Type == "CASE_RESOLUTION_CHANGED");
    }

    [Fact]
    public async Task SecondFeedbackSubmissionThrows()
    {
        var repository = new FakeCaseRepository();
        var feedbackRepository = new FakeFeedbackRepository();
        var @case = NewCase();
        @case.CompleteByUser(null, DateTimeOffset.UtcNow);
        repository.Add(@case);
        var sut = new SubmitFeedbackUseCase(repository, feedbackRepository, new FakeUnitOfWork(), new FakeTurnEventStream(), new FakeOutbox(), TimeProvider.System);
        await sut.ExecuteAsync(@case.Id, "owner-1", null, null, null, null, CancellationToken.None);

        await Assert.ThrowsAsync<FeedbackAlreadySubmittedException>(() =>
            sut.ExecuteAsync(@case.Id, "owner-1", null, null, null, "second try", CancellationToken.None));
    }

    [Fact]
    public async Task FeedbackBeforeCompletionThrows()
    {
        var repository = new FakeCaseRepository();
        var @case = NewCase();
        repository.Add(@case);
        var sut = new SubmitFeedbackUseCase(repository, new FakeFeedbackRepository(), new FakeUnitOfWork(), new FakeTurnEventStream(), new FakeOutbox(), TimeProvider.System);

        await Assert.ThrowsAsync<CaseNotCompletedException>(() =>
            sut.ExecuteAsync(@case.Id, "owner-1", null, null, true, null, CancellationToken.None));
    }
}
