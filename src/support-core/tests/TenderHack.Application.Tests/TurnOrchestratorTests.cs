using TenderHack.Application.Knowledge;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Ports;
using TenderHack.Application.Tests.Fakes;
using TenderHack.Domain.Cases;
using Xunit;

namespace TenderHack.Application.Tests;

public sealed class TurnOrchestratorTests
{
    private readonly FakeKnowledgeService _knowledge = new();
    private readonly FakeModerationRuleEngine _moderation = new();
    private readonly FakeTurnEventStream _events = new();

    private TurnOrchestrator CreateSut(int closeAfterWarnings = 1) =>
        new(_knowledge, _moderation, _events, new ModerationOptions(closeAfterWarnings), TimeProvider.System);

    private static Case NewCase() => new(CaseId.New(), ownerId: "owner-1", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ConfirmedFirstViolationWarnsAndSkipsKnowledgeCalls()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _moderation.NextResult = new ModerationRuleMatch(Confirmed: true, "v1", ["слово"]);

        var outcome = await sut.RunAsync(@case, "оскорбление", CancellationToken.None);

        Assert.Equal(Decision.ModerationWarning, outcome.Decision);
        Assert.Equal(1, outcome.ModerationWarningCount);
        Assert.Equal(ConversationStatus.Active, @case.ConversationStatus);
        Assert.Contains(_events.Published, e => e.Event.Type == "MODERATION_WARNING");
    }

    [Fact]
    public async Task ConfirmedViolationAtThresholdClosesConversation()
    {
        var sut = CreateSut(closeAfterWarnings: 1);
        var @case = NewCase();
        _moderation.NextResult = new ModerationRuleMatch(Confirmed: true, "v1", ["слово"]);
        await sut.RunAsync(@case, "первое", CancellationToken.None);

        var outcome = await sut.RunAsync(@case, "второе", CancellationToken.None);

        Assert.Equal(Decision.ModerationClose, outcome.Decision);
        Assert.Equal(ConversationStatus.ClosedModeration, @case.ConversationStatus);
        Assert.Contains(_events.Published, e => e.Event.Type == "CONVERSATION_CLOSED");
    }

    [Fact]
    public async Task SufficientEvidenceWithSupportedClaimsAnswers()
    {
        var sut = CreateSut();
        var @case = NewCase();

        var outcome = await sut.RunAsync(@case, "как подать заявку?", CancellationToken.None);

        Assert.Equal(Decision.Answer, outcome.Decision);
        Assert.Equal("Ответ.", outcome.AnswerMarkdown);
        Assert.Equal(["frag-1"], outcome.SourceFragmentIds);
        Assert.Equal(ResolutionStatus.Unknown, @case.ResolutionStatus);
        Assert.Contains(_events.Published, e => e.Event.Type == "AI_ANSWER");
    }

    [Fact]
    public async Task UnsupportedClaimFallsBackToHandoffOfferInsteadOfPublishingUnverifiedAnswer()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Verify = new VerifyResult([new ClaimVerification("claim-1", Supported: false, [])]);

        var outcome = await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        Assert.Equal(Decision.HandoffOffer, outcome.Decision);
        Assert.Null(outcome.AnswerMarkdown);
    }

    [Fact]
    public async Task ConditionDependentEvidenceClarifiesWithoutCallingDraft()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.ConditionDependent, [], ["сумма контракта"], []);

        var outcome = await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        Assert.Equal(Decision.Clarify, outcome.Decision);
        Assert.Equal(["сумма контракта"], outcome.MissingConditions);
        Assert.DoesNotContain(_events.Published, e => e.Event.Type == "AI_ANSWER");
    }

    [Fact]
    public async Task InsufficientEvidenceOffersHandoffWithoutCallingDraft()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Insufficient, [], [], []);

        var outcome = await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        Assert.Equal(Decision.HandoffOffer, outcome.Decision);
        Assert.DoesNotContain(_events.Published, e => e.Event.Type == "AI_ANSWER");
    }

    [Fact]
    public async Task KnowledgeFailureProducesTechnicalErrorNotNoAnswer()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.FailAt = new KnowledgeFailureException(KnowledgeFailureCategory.Timeout, "boom");
        _knowledge.FailingStage = nameof(FakeKnowledgeService.RetrieveAsync);

        var outcome = await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        Assert.Equal(Decision.TechnicalError, outcome.Decision);
        Assert.Equal(KnowledgeFailureCategory.Timeout, outcome.FailureCategory);
        Assert.Equal(TurnStatus.Failed, @case.ActiveTurn!.Status);
        Assert.Contains(_events.Published, e => e.Event.Type == "TECHNICAL_ERROR");
    }

    [Fact]
    public async Task EachRunStartsANewRevision()
    {
        // The Domain-level supersede invariant (a turn left Queued/Running gets superseded by the
        // next one) is covered in TenderHack.Domain.Tests; here the orchestrator always runs a turn
        // to a terminal state synchronously, so each call simply appends the next revision.
        var sut = CreateSut();
        var @case = NewCase();

        await sut.RunAsync(@case, "первое", CancellationToken.None);
        await sut.RunAsync(@case, "второе", CancellationToken.None);

        Assert.Equal(2, @case.Turns.Count);
        Assert.Equal(1, @case.Turns[0].Revision);
        Assert.Equal(2, @case.Turns[1].Revision);
    }
}
