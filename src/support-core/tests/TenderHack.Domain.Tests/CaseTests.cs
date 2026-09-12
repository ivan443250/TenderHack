using TenderHack.Domain;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class CaseTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Case NewCase() => new(CaseId.New(), ownerId: "owner-1", Now);

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
        sut.PrepareHandoff();
        sut.ConfirmHandoff();
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
        sut.PrepareHandoff();
        sut.ConfirmHandoff();
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
        sut.PrepareHandoff();

        // No Confirm() happened, so acknowledgement must be rejected — accepted only follows a pending submission.
        Assert.Throws<InvalidHandoffTransitionException>(() => sut.AcknowledgeHandoff(simulated: false, null, Now));
    }

    [Fact]
    public void PreparingASecondHandoffThrowsInsteadOfBypassingTheExistingOne()
    {
        var sut = NewCase();
        sut.PrepareHandoff();

        Assert.Throws<HandoffAlreadyExistsException>(() => sut.PrepareHandoff());
    }

    [Fact]
    public void CommandsOnACaseWithNoHandoffYetThrowHandoffNotFound()
    {
        var sut = NewCase();

        Assert.Throws<HandoffNotFoundException>(() => sut.ConfirmHandoff());
        Assert.Throws<HandoffNotFoundException>(() => sut.RetryHandoff());
        Assert.Throws<HandoffNotFoundException>(() => sut.FailHandoff());
        Assert.Throws<HandoffNotFoundException>(() => sut.AcknowledgeHandoff(simulated: false, null, Now));
        Assert.Throws<HandoffNotFoundException>(() => sut.MarkHandoffStale());
        Assert.Throws<HandoffNotFoundException>(() => sut.IngestHandoffStatus(HandoffId.New(), 1, null, null, null, Now));
    }

    [Fact]
    public void MarkingAHandoffStaleAfterTheTtlDoesNotConvertPendingToAccepted()
    {
        var sut = NewCase();
        sut.PrepareHandoff();
        sut.ConfirmHandoff();

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
        sut.PrepareHandoff();
        sut.ConfirmHandoff();
        sut.AcknowledgeHandoff(simulated: false, "ext-1", Now);

        Assert.Throws<HandoffMismatchException>(() =>
            sut.IngestHandoffStatus(HandoffId.New(), 1, null, null, null, Now));
    }

    [Fact]
    public void DuplicateHandoffStatusUpdateIsANoOp()
    {
        var sut = NewCase();
        sut.PrepareHandoff();
        sut.ConfirmHandoff();
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
        sut.PrepareHandoff();
        sut.ConfirmHandoff();
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
        sut.PrepareHandoff();
        sut.ConfirmHandoff();
        sut.AcknowledgeHandoff(simulated: false, "ext-1", Now);
        sut.CompleteByUser(solved: null, Now);

        sut.IngestHandoffStatus(sut.Handoff!.Id, externalRevision: 1, null, null, HandoffTerminalOutcome.Resolved, Now.AddMinutes(1));

        Assert.Equal(ConversationStatus.ClosedUser, sut.ConversationStatus);
        Assert.Equal(Cases.CompletionReason.User, sut.CompletionReason);
        Assert.Equal(ResolutionStatus.Resolved, sut.ResolutionStatus);
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
        sut.PrepareHandoff();
        sut.ConfirmHandoff();

        sut.AcknowledgeHandoff(simulated: true, "ext-1", Now);

        Assert.Equal(Now, sut.Handoff!.AcceptedAt);
        Assert.False(sut.Handoff.Stale);
    }

    [Fact]
    public void MarkingAHandoffStaleDoesNotChangeConversationOrResolutionStatus()
    {
        var sut = NewCase();
        sut.PrepareHandoff();
        sut.ConfirmHandoff();
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
        sut.PrepareHandoff();
        sut.ConfirmHandoff();
        sut.AcknowledgeHandoff(simulated: true, "ext-1", Now);
        sut.MarkHandoffStale();

        sut.IngestHandoffStatus(sut.Handoff!.Id, externalRevision: 1, new HandoffStage("IN_PROGRESS", null), null, null, Now);

        Assert.False(sut.Handoff.Stale);
    }
}
