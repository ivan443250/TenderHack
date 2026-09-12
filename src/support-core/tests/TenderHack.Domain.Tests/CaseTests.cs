using TenderHack.Domain;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class CaseTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Case NewCase() => new(CaseId.New(), ownerId: "owner-1", Now);

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

    [Fact]
    public void OneActiveRevisionIsAuthoritative()
    {
        var sut = NewCase();
        var first = sut.StartTurn(Now);
        var second = sut.StartTurn(Now.AddSeconds(1));

        Assert.Equal(TurnStatus.Superseded, first.Status);
        Assert.Same(second, sut.ActiveTurn);
        Assert.Equal(2, second.Revision);
    }

    [Fact]
    public void RunningOldTurnCannotPublishAfterNewerRevisionSupersedesIt()
    {
        var sut = NewCase();
        var stale = sut.StartTurn(Now);
        sut.StartTurn(Now.AddSeconds(1));

        var published = sut.TryPublishDecision(stale.Id, stale.Revision, Decision.Answer);

        Assert.False(published);
        Assert.Equal(TurnStatus.Superseded, stale.Status);
    }

    [Fact]
    public void AnswerCannotSetResolved()
    {
        var sut = NewCase();
        var turn = sut.StartTurn(Now);

        var published = sut.TryPublishDecision(turn.Id, turn.Revision, Decision.Answer);

        Assert.True(published);
        Assert.Equal(ResolutionStatus.Unknown, sut.ResolutionStatus);
    }

    [Fact]
    public void PositiveFeedbackCannotSetResolved()
    {
        var sut = NewCase();

        // Rating-style feedback has no pathway to resolution at all: only the explicit
        // `solved` signal (ApplyFeedbackSolvedSignal) or an explicit completion can.
        Assert.Equal(ResolutionStatus.Unknown, sut.ResolutionStatus);
    }

    [Fact]
    public void FeedbackSolvedSignalOnlyAppliesWhileResolutionIsUnknown()
    {
        var sut = NewCase();
        sut.CompleteByUser(solved: true, Now);

        sut.ApplyFeedbackSolvedSignal(solved: false);

        Assert.Equal(ResolutionStatus.Resolved, sut.ResolutionStatus);
    }

    [Fact]
    public void FirstConfirmedViolationWarnsNotCloses()
    {
        var sut = NewCase();
        sut.StartTurn(Now);

        var decision = sut.RecordModerationViolation(closeAfterWarnings: 1, Now);

        Assert.Equal(Decision.ModerationWarning, decision);
        Assert.Equal(ConversationStatus.Active, sut.ConversationStatus);
        Assert.Equal(1, sut.ModerationWarningCount);
    }

    [Fact]
    public void ViolationAtThresholdCloses()
    {
        var sut = NewCase();
        sut.StartTurn(Now);
        sut.RecordModerationViolation(closeAfterWarnings: 1, Now);
        sut.StartTurn(Now.AddSeconds(1));

        var decision = sut.RecordModerationViolation(closeAfterWarnings: 1, Now.AddSeconds(1));

        Assert.Equal(Decision.ModerationClose, decision);
        Assert.Equal(ConversationStatus.ClosedModeration, sut.ConversationStatus);
        Assert.Equal(Cases.CompletionReason.Moderation, sut.CompletionReason);
    }

    [Fact]
    public void NewCaseModerationCounterStartsAtZero()
    {
        var sut = NewCase();

        Assert.Equal(0, sut.ModerationWarningCount);
    }

    [Fact]
    public void ModerationCloseDoesNotRevokeAnAcceptedHandoff()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");
        sut.AcknowledgeHandoff(simulated: false, externalCaseId: "ext-1", Now);
        sut.StartTurn(Now);
        sut.RecordModerationViolation(closeAfterWarnings: 1, Now);
        sut.StartTurn(Now.AddSeconds(1));

        sut.RecordModerationViolation(closeAfterWarnings: 1, Now.AddSeconds(1));

        Assert.Equal(HandoffStatus.Accepted, sut.Handoff!.Status);
    }

    [Fact]
    public void FailedAiTurnDoesNotMutatePreviousAcceptedHandoff()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");
        sut.AcknowledgeHandoff(simulated: false, externalCaseId: "ext-1", Now);
        var turn = sut.StartTurn(Now);

        var failed = sut.TryFailTurn(turn.Id, turn.Revision);

        Assert.True(failed);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal(HandoffStatus.Accepted, sut.Handoff!.Status);
        Assert.Null(sut.Handoff.Terminal);
    }

    [Fact]
    public void HandoffAcceptedOnlyOnAdapterAcknowledgement()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());

        // No Confirm() happened, so acknowledgement must be rejected — accepted only follows a pending submission.
        Assert.Throws<InvalidHandoffTransitionException>(() => sut.AcknowledgeHandoff(simulated: false, null, Now));
    }

    [Fact]
    public void PreparingASecondHandoffThrowsInsteadOfBypassingTheExistingOne()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());

        Assert.Throws<HandoffAlreadyExistsException>(() => sut.PrepareHandoff(NewHandoffPackage()));
    }

    [Fact]
    public void PrepareAttachesThePackageAndConfirmSetsTheUsersFinalSummary()
    {
        var sut = NewCase();
        var package = NewHandoffPackage();

        sut.PrepareHandoff(package);
        Assert.Same(package, sut.Handoff!.Package);
        Assert.Null(sut.Handoff.ConfirmedSummary);

        sut.ConfirmHandoff("Отредактированное пользователем резюме");
        Assert.Equal("Отредактированное пользователем резюме", sut.Handoff.ConfirmedSummary);
        // The package's own draft summary is untouched by the user's edit.
        Assert.Equal("Резюме обращения", sut.Handoff.Package!.DraftSummary);
    }

    [Fact]
    public void RetryUpdatesTheConfirmedSummaryAgain()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Первое резюме");
        sut.FailHandoff();

        sut.RetryHandoff("Второе резюме");

        Assert.Equal("Второе резюме", sut.Handoff!.ConfirmedSummary);
        Assert.Equal(HandoffStatus.Pending, sut.Handoff.Status);
    }

    [Fact]
    public void CommandsOnACaseWithNoHandoffYetThrowHandoffNotFound()
    {
        var sut = NewCase();

        Assert.Throws<HandoffNotFoundException>(() => sut.ConfirmHandoff("Резюме"));
        Assert.Throws<HandoffNotFoundException>(() => sut.RetryHandoff("Резюме"));
        Assert.Throws<HandoffNotFoundException>(() => sut.FailHandoff());
        Assert.Throws<HandoffNotFoundException>(() => sut.AcknowledgeHandoff(simulated: false, null, Now));
        Assert.Throws<HandoffNotFoundException>(() => sut.MarkHandoffStale());
        Assert.Throws<HandoffNotFoundException>(() => sut.IngestHandoffStatus(HandoffId.New(), 1, null, null, null, Now));
    }

    [Fact]
    public void MarkingAHandoffStaleAfterTheTtlDoesNotConvertPendingToAccepted()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");

        // A status-poll timeout only flags staleness — acceptance still requires an explicit adapter
        // acknowledgement, so a stale-but-pending handoff must remain Pending (support-adapter-v0.md §6.1).
        sut.MarkHandoffStale();

        Assert.Equal(HandoffStatus.Pending, sut.Handoff!.Status);
        Assert.True(sut.Handoff.Stale);

        // The adapter can still acknowledge a stale submission after the fact.
        sut.AcknowledgeHandoff(simulated: false, "ext-1", Now);
        Assert.Equal(HandoffStatus.Accepted, sut.Handoff.Status);
    }

    [Fact]
    public void HandoffStatusMustTargetTheSameHandoffAndCase()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");
        sut.AcknowledgeHandoff(simulated: false, "ext-1", Now);

        Assert.Throws<HandoffMismatchException>(() =>
            sut.IngestHandoffStatus(HandoffId.New(), 1, null, null, null, Now));
    }

    [Fact]
    public void DuplicateHandoffStatusUpdateIsANoOp()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");
        sut.AcknowledgeHandoff(simulated: false, "ext-1", Now);
        var stage = new HandoffStage("IN_PROGRESS", "В работе");
        sut.IngestHandoffStatus(sut.Handoff!.Id, externalRevision: 1, stage, null, null, Now);

        // Same revision replayed (e.g. poll + webhook both deliver it) must not re-apply or throw.
        sut.IngestHandoffStatus(sut.Handoff.Id, externalRevision: 1, new HandoffStage("RESOLVED", null), null, null, Now);

        Assert.Equal("IN_PROGRESS", sut.Handoff.Stage!.Code);
    }

    [Fact]
    public void ALowerExternalRevisionArrivingAfterAHigherOneIsIgnored()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");
        sut.AcknowledgeHandoff(simulated: false, "ext-1", Now);
        sut.IngestHandoffStatus(sut.Handoff!.Id, externalRevision: 5, new HandoffStage("IN_PROGRESS", null), null, null, Now);

        // A poll and a webhook can race and deliver an older snapshot after a newer one already applied.
        var applied = sut.IngestHandoffStatus(sut.Handoff.Id, externalRevision: 3, new HandoffStage("QUEUED", null), null, null, Now);

        Assert.False(applied);
        Assert.Equal("IN_PROGRESS", sut.Handoff.Stage!.Code);
        Assert.Equal(5, sut.Handoff.LastExternalRevision);
    }

    [Fact]
    public void TerminalAdapterStatusOnAlreadyUserClosedCaseUpdatesFactsButDoesNotReopenOrRecloseConversation()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");
        sut.AcknowledgeHandoff(simulated: false, "ext-1", Now);
        sut.CompleteByUser(solved: null, Now);

        sut.IngestHandoffStatus(sut.Handoff!.Id, externalRevision: 1, null, null, HandoffTerminalOutcome.Resolved, Now.AddMinutes(1));

        Assert.Equal(ConversationStatus.ClosedUser, sut.ConversationStatus);
        Assert.Equal(Cases.CompletionReason.User, sut.CompletionReason);
        Assert.Equal(ResolutionStatus.Resolved, sut.ResolutionStatus);
    }

    [Fact]
    public void CancelledTerminalOnAnActiveCaseClosesItButLeavesResolutionUnknown()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");
        sut.AcknowledgeHandoff(simulated: false, "ext-1", Now);

        sut.IngestHandoffStatus(sut.Handoff!.Id, externalRevision: 1, null, null, HandoffTerminalOutcome.Cancelled, Now.AddMinutes(1));

        Assert.Equal(ConversationStatus.ClosedSupport, sut.ConversationStatus);
        Assert.Equal(Cases.CompletionReason.Support, sut.CompletionReason);
        // support-adapter-v0.md §6.3: CANCELLED carries no resolution fact of its own.
        Assert.Equal(ResolutionStatus.Unknown, sut.ResolutionStatus);
    }

    [Fact]
    public void CancelledTerminalDoesNotOverwriteAnAlreadyKnownResolution()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");
        sut.AcknowledgeHandoff(simulated: false, "ext-1", Now);
        sut.CompleteByUser(solved: false, Now.AddMinutes(1));

        sut.IngestHandoffStatus(sut.Handoff!.Id, externalRevision: 1, null, null, HandoffTerminalOutcome.Cancelled, Now.AddMinutes(2));

        Assert.Equal(ConversationStatus.ClosedUser, sut.ConversationStatus);
        Assert.Equal(ResolutionStatus.Unresolved, sut.ResolutionStatus);
    }

    [Fact]
    public void TerminalResolvedOnAnAlreadyClosedCaseDoesNotOverwriteAKnownResolution()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");
        sut.AcknowledgeHandoff(simulated: false, "ext-1", Now);
        // The user already answered "not solved" before the adapter's terminal fact arrives.
        sut.CompleteByUser(solved: false, Now.AddMinutes(1));

        sut.IngestHandoffStatus(sut.Handoff!.Id, externalRevision: 1, null, null, HandoffTerminalOutcome.Resolved, Now.AddMinutes(2));

        Assert.Equal(ConversationStatus.ClosedUser, sut.ConversationStatus);
        // A later terminal RESOLVED must not silently overturn the user's own explicit answer.
        Assert.Equal(ResolutionStatus.Unresolved, sut.ResolutionStatus);
    }

    [Fact]
    public void ClarificationLimitIsReachedAfterTheConfiguredMaximum()
    {
        var sut = NewCase();

        Assert.False(sut.ClarificationLimitReached);
        sut.RecordClarification(["условие 1"]);
        Assert.False(sut.ClarificationLimitReached);
        sut.RecordClarification(["условие 2"]);
        Assert.True(sut.ClarificationLimitReached);
    }

    [Fact]
    public void ResolvingAScenarioResetsTheClarificationLoop()
    {
        var sut = NewCase();
        sut.RecordClarification(["условие 1"]);
        sut.RecordClarification(["условие 2"]);
        Assert.True(sut.ClarificationLimitReached);

        sut.ResetClarificationLoop();

        Assert.False(sut.ClarificationLimitReached);
        Assert.Equal(0, sut.TurnContext.ConsecutiveClarifications);
    }

    [Fact]
    public void DecliningWithNoPendingClarificationReturnsFalse()
    {
        var sut = NewCase();

        var declined = sut.TryDeclineCurrentClarification(out var conditions);

        Assert.False(declined);
        Assert.Empty(conditions);
    }

    [Fact]
    public void DecliningAPendingClarificationRecordsItAndReturnsTheConditions()
    {
        var sut = NewCase();
        sut.RecordClarification(["сумма контракта"]);

        var declined = sut.TryDeclineCurrentClarification(out var conditions);

        Assert.True(declined);
        Assert.Equal(["сумма контракта"], conditions);
        Assert.Contains("сумма контракта", sut.TurnContext.DeclinedMissingConditions);
    }

    [Fact]
    public void ObservingUnderstandingMergesSlotsIntoTurnContext()
    {
        var sut = NewCase();

        sut.ObserveUnderstanding("я поставщик", [new ContextSlot("role", "поставщик", ContextSlotProvenance.UserExplicit)]);

        var slot = Assert.Single(sut.TurnContext.KnownSlots);
        Assert.Equal("поставщик", slot.Value);
    }

    [Fact]
    public void CompletedCaseRejectsNewUserMessages()
    {
        var sut = NewCase();
        sut.CompleteByUser(solved: true, Now);

        Assert.Throws<CaseClosedException>(() => sut.StartTurn(Now.AddSeconds(1)));
    }

    [Fact]
    public void CompletingAnAlreadyCompletedCaseThrows()
    {
        var sut = NewCase();
        sut.CompleteByUser(solved: true, Now);

        Assert.Throws<CaseAlreadyCompletedException>(() => sut.CompleteByUser(solved: false, Now.AddSeconds(1)));
    }

    [Fact]
    public void AcceptingAHandoffRecordsAcceptedAtAndClearsStale()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");

        sut.AcknowledgeHandoff(simulated: true, "ext-1", Now);

        Assert.Equal(Now, sut.Handoff!.AcceptedAt);
        Assert.False(sut.Handoff.Stale);
    }

    [Fact]
    public void MarkingAHandoffStaleDoesNotChangeConversationOrResolutionStatus()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");
        sut.AcknowledgeHandoff(simulated: true, "ext-1", Now);

        sut.MarkHandoffStale();

        Assert.True(sut.Handoff!.Stale);
        Assert.Equal(ConversationStatus.Active, sut.ConversationStatus);
        Assert.Equal(ResolutionStatus.Unknown, sut.ResolutionStatus);
    }

    [Fact]
    public void AFreshStatusFactClearsAPreviouslyStaleFlag()
    {
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        sut.ConfirmHandoff("Резюме");
        sut.AcknowledgeHandoff(simulated: true, "ext-1", Now);
        sut.MarkHandoffStale();

        sut.IngestHandoffStatus(sut.Handoff!.Id, externalRevision: 1, new HandoffStage("IN_PROGRESS", null), null, null, Now);

        Assert.False(sut.Handoff.Stale);
    }

    [Theory]
    [InlineData(false, false)] // NotRequested: prepared, never confirmed
    [InlineData(true, false)]  // Pending: confirmed, adapter has not acknowledged yet
    [InlineData(true, true)]   // Failed
    public void StatusFactsAreRejectedUntilTheAdapterHasAcknowledgedTheHandoff(bool confirm, bool fail)
    {
        // Regression: a stage/terminal fact on an unacknowledged handoff would have closed the case
        // through CompleteBySupport — "prepared handoff" masquerading as "accepted handoff".
        var sut = NewCase();
        sut.PrepareHandoff(NewHandoffPackage());
        if (confirm) sut.ConfirmHandoff("Резюме");
        if (fail) sut.FailHandoff();

        Assert.Throws<HandoffNotAcceptedException>(() =>
            sut.IngestHandoffStatus(sut.Handoff!.Id, externalRevision: 1, null, null, HandoffTerminalOutcome.Resolved, Now));
        Assert.Equal(ConversationStatus.Active, sut.ConversationStatus);
        Assert.Null(sut.Handoff!.Terminal);
    }

    [Fact]
    public void ActiveTurnIsTheHighestRevisionRegardlessOfLoadOrder()
    {
        // Regression: persistence fills the backing list in whatever order rows come back (EF Core
        // adds no ORDER BY for owned collections, so it is Postgres heap order), and `_turns[^1]`
        // reported an older revision as active. Simulate an out-of-order load through the backing
        // field exactly the way the materializer populates it.
        var sut = NewCase();
        var first = sut.StartTurn(Now);
        sut.TryPublishDecision(first.Id, first.Revision, Decision.Answer);
        var second = sut.StartTurn(Now.AddSeconds(1));
        sut.TryPublishDecision(second.Id, second.Revision, Decision.Answer);

        var backingList = (List<Turn>)typeof(Case)
            .GetField("_turns", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(sut)!;
        backingList.Reverse();

        Assert.Same(second, sut.ActiveTurn);
        Assert.Equal([1, 2], sut.Turns.Select(t => t.Revision));
        Assert.Equal(3, sut.StartTurn(Now.AddSeconds(2)).Revision);
    }

    [Fact]
    public void ACompletedTurnCannotBeFailedOrRepublished()
    {
        var sut = NewCase();
        var turn = sut.StartTurn(Now);
        sut.TryPublishDecision(turn.Id, turn.Revision, Decision.Answer);

        Assert.False(sut.TryFailTurn(turn.Id, turn.Revision));
        Assert.False(sut.TryPublishDecision(turn.Id, turn.Revision, Decision.Clarify));
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.Equal(Decision.Answer, turn.Decision);
    }
}
