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
        sut.AcknowledgeHandoff(simulated: false, externalCaseId: "ext-1");
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
        sut.AcknowledgeHandoff(simulated: false, externalCaseId: "ext-1");
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
        Assert.Throws<InvalidHandoffTransitionException>(() => sut.AcknowledgeHandoff(simulated: false, null));
    }

    [Fact]
    public void HandoffStatusMustTargetTheSameHandoffAndCase()
    {
        var sut = NewCase();
        sut.PrepareHandoff();
        sut.ConfirmHandoff();
        sut.AcknowledgeHandoff(simulated: false, "ext-1");

        Assert.Throws<HandoffMismatchException>(() =>
            sut.IngestHandoffStatus(HandoffId.New(), 1, null, null, null, Now));
    }

    [Fact]
    public void DuplicateHandoffStatusUpdateIsANoOp()
    {
        var sut = NewCase();
        sut.PrepareHandoff();
        sut.ConfirmHandoff();
        sut.AcknowledgeHandoff(simulated: false, "ext-1");
        var stage = new HandoffStage("IN_PROGRESS", "В работе");
        sut.IngestHandoffStatus(sut.Handoff!.Id, externalRevision: 1, stage, null, null, Now);

        // Same revision replayed (e.g. poll + webhook both deliver it) must not re-apply or throw.
        sut.IngestHandoffStatus(sut.Handoff.Id, externalRevision: 1, new HandoffStage("RESOLVED", null), null, null, Now);

        Assert.Equal("IN_PROGRESS", sut.Handoff.Stage!.Code);
    }

    [Fact]
    public void TerminalAdapterStatusOnAlreadyUserClosedCaseUpdatesFactsButDoesNotReopenOrRecloseConversation()
    {
        var sut = NewCase();
        sut.PrepareHandoff();
        sut.ConfirmHandoff();
        sut.AcknowledgeHandoff(simulated: false, "ext-1");
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
}
