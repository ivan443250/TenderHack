using TenderHack.Application.Orchestration;
using TenderHack.Application.Tests.Fakes;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;
using Xunit;

namespace TenderHack.Application.Tests;

/// <summary>
/// `HandoffSubmissionPublisher` is the event/notification seam `HandoffSubmitWorker` (api-worker)
/// calls right after <see cref="Ports.IHandoffAdapter.SubmitAsync"/> — without it the browser would
/// sit on `PENDING` until the first polled stage arrives, or forever for a `FAILED` submission
/// (web-api-v0.md §4.2, §8; architecture.md §5.9).
/// </summary>
public sealed class HandoffSubmissionPublisherTests
{
    private static Case NewCaseWithPendingHandoff(string ownerId = "owner-1")
    {
        var @case = new Case(CaseId.New(), ownerId, DateTimeOffset.UtcNow);
        var package = new HandoffPackage(
            DraftSummary: "Резюме обращения", UserReportedContext: [], VerifiedPortalContext: [],
            AlreadyTried: [], UnknownFields: [], SourcesChecked: [], HandoffReason: ["INSUFFICIENT_EVIDENCE"],
            RelevantMessageIds: [], Channel: HandoffPackage.PortalChatChannel, DispatchQueue: "l2-general",
            EngineeringReviewSuggested: false);
        @case.PrepareHandoff(package);
        @case.ConfirmHandoff("Резюме");
        return @case;
    }

    [Fact]
    public async Task RealAcceptancePublishesHandoffStatusAndNotification()
    {
        var @case = NewCaseWithPendingHandoff();
        @case.AcknowledgeHandoff(simulated: false, "ext-1", DateTimeOffset.UtcNow);
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var sut = new HandoffSubmissionPublisher(events, notifications);

        await sut.PublishAsync(@case, safeMessage: null, DateTimeOffset.UtcNow, CancellationToken.None);

        var published = Assert.Single(events.Published);
        Assert.Equal("HANDOFF_STATUS", published.Event.Type);
        Assert.Equal("Accepted", published.Event.Payload["status"]);
        Assert.Equal(new[] { "status" }, published.Event.Payload["changed"]);

        var notification = Assert.Single(notifications.Enqueued);
        Assert.Equal("HANDOFF_UPDATED", notification.Type);
        Assert.Equal("Real", notification.IntegrationMode);
    }

    [Fact]
    public async Task SimulatedAcceptancePublishesDemoLabelledNotification()
    {
        var @case = NewCaseWithPendingHandoff();
        @case.AcknowledgeHandoff(simulated: true, "demo-ext-1", DateTimeOffset.UtcNow);
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var sut = new HandoffSubmissionPublisher(events, notifications);

        await sut.PublishAsync(@case, safeMessage: null, DateTimeOffset.UtcNow, CancellationToken.None);

        var published = Assert.Single(events.Published);
        Assert.Equal("SimulatedAccepted", published.Event.Payload["status"]);

        var notification = Assert.Single(notifications.Enqueued);
        Assert.Equal("Simulated", notification.IntegrationMode);
        Assert.Contains("демо", notification.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedSubmissionPublishesTheAdapterSafeMessage()
    {
        var @case = NewCaseWithPendingHandoff();
        @case.FailHandoff();
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var sut = new HandoffSubmissionPublisher(events, notifications);

        await sut.PublishAsync(@case, safeMessage: "Демо-адаптер отклонил обращение.", DateTimeOffset.UtcNow, CancellationToken.None);

        var published = Assert.Single(events.Published);
        Assert.Equal("Failed", published.Event.Payload["status"]);
        Assert.Equal("Демо-адаптер отклонил обращение.", published.Event.Payload["safe_message"]);

        var notification = Assert.Single(notifications.Enqueued);
        Assert.Equal("Демо-адаптер отклонил обращение.", notification.Body);
    }

    [Fact]
    public async Task TransportExceptionPathFailsWithoutASafeMessageStillGetsAGenericNotification()
    {
        var @case = NewCaseWithPendingHandoff();
        @case.FailHandoff();
        var events = new FakeTurnEventStream();
        var notifications = new FakeNotificationSink();
        var sut = new HandoffSubmissionPublisher(events, notifications);

        await sut.PublishAsync(@case, safeMessage: null, DateTimeOffset.UtcNow, CancellationToken.None);

        var notification = Assert.Single(notifications.Enqueued);
        Assert.Equal("Поддержка не подтвердила приём. Можно повторить отправку.", notification.Body);
    }
}
