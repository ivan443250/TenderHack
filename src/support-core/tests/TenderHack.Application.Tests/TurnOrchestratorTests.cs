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
    private readonly FakeOutbox _outbox = new();

    private TurnOrchestrator CreateSut(int closeAfterWarnings = 1) =>
        new(_knowledge, _moderation, _events, _outbox, new ModerationOptions(closeAfterWarnings), TimeProvider.System);

    private static Case NewCase() => new(CaseId.New(), ownerId: "owner-1", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ConfirmedFirstViolationWarnsAndSkipsKnowledgeCalls()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _moderation.NextResult = new ModerationRuleMatch("PROFANITY_001", "v1", "слово", 0, 5, RequiresContextCheck: false);

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
        _moderation.NextResult = new ModerationRuleMatch("PROFANITY_001", "v1", "слово", 0, 5, RequiresContextCheck: false);
        await sut.RunAsync(@case, "первое", CancellationToken.None);

        var outcome = await sut.RunAsync(@case, "второе", CancellationToken.None);

        Assert.Equal(Decision.ModerationClose, outcome.Decision);
        Assert.Equal(ConversationStatus.ClosedModeration, @case.ConversationStatus);
        Assert.Contains(_events.Published, e => e.Event.Type == "CONVERSATION_CLOSED");
    }

    [Fact]
    public async Task AmbiguousMatchConfirmedOffensiveByKnowledgeWarns()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _moderation.NextResult = new ModerationRuleMatch("PROFANITY_A01", "v1", "дура", 0, 4, RequiresContextCheck: true);
        _knowledge.ModerationContext = new ModerationContextResult(ModerationAmbiguity.Offensive, "stub-v0");

        var outcome = await sut.RunAsync(@case, "ты дура", CancellationToken.None);

        Assert.Equal(Decision.ModerationWarning, outcome.Decision);
    }

    [Theory]
    [InlineData(ModerationAmbiguity.Uncertain)]
    [InlineData(ModerationAmbiguity.NotOffensive)]
    public async Task AmbiguousMatchNotConfirmedOffensiveContinuesAsNormalTurn(ModerationAmbiguity ambiguity)
    {
        var sut = CreateSut();
        var @case = NewCase();
        _moderation.NextResult = new ModerationRuleMatch("PROFANITY_A01", "v1", "тупой", 0, 5, RequiresContextCheck: true);
        _knowledge.ModerationContext = new ModerationContextResult(ambiguity, "stub-v0");

        var outcome = await sut.RunAsync(@case, "тупой баг в форме", CancellationToken.None);

        Assert.Equal(Decision.Answer, outcome.Decision);
        Assert.Equal(0, outcome.ModerationWarningCount);
    }

    [Fact]
    public async Task ExplicitHumanRequestOffersHandoffWithoutCallingKnowledge()
    {
        var sut = CreateSut();
        var @case = NewCase();

        var outcome = await sut.RunAsync(@case, "соедините меня с оператором", CancellationToken.None);

        Assert.Equal(Decision.HandoffOffer, outcome.Decision);
        Assert.DoesNotContain(_events.Published, e => e.Event.Type == "TURN_STAGE"); // understand/retrieve never ran
        var handoffEvent = _events.Published.First(e => e.Event.Type == "HANDOFF_OFFER");
        var reasonCodes = Assert.IsAssignableFrom<IReadOnlyList<string>>(handoffEvent.Event.Payload["reason_codes"]);
        Assert.Contains("EXPLICIT_HUMAN_REQUEST", reasonCodes);
    }

    [Fact]
    public async Task SufficientEvidenceWithRiskFlagsAnswersAndOffersHandoff()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], ["needs_portal_verification"]);

        var outcome = await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        Assert.Equal(Decision.AnswerAndHandoff, outcome.Decision);
        Assert.Equal("Ответ.", outcome.AnswerMarkdown);
        Assert.Contains(_events.Published, e => e.Event.Type == "AI_ANSWER");
        Assert.Contains(_events.Published, e => e.Event.Type == "HANDOFF_OFFER");
    }

    [Fact]
    public async Task InsufficientEvidencePublishesHandoffOfferWithRoutingReasonCode()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Insufficient, [], [], []);

        await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        var handoffEvent = _events.Published.First(e => e.Event.Type == "HANDOFF_OFFER");
        var reasonCodes = Assert.IsAssignableFrom<IReadOnlyList<string>>(handoffEvent.Event.Payload["reason_codes"]);
        Assert.Contains("INSUFFICIENT_EVIDENCE", reasonCodes);
        Assert.Equal("L2", handoffEvent.Event.Payload["recommended_line"]);
    }

    [Fact]
    public async Task EntitySlotTypeAndProvenanceSurviveFromUnderstandIntoRetrieve()
    {
        // Regression: earlier code flattened Entity to a bare string on the .NET boundary, so
        // retrieve always re-sent type="text"/provenance=Unknown regardless of what understand
        // actually extracted — silently discarding exact-slot retrieval signal.
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.UnderstandOverride = req => new UnderstandResult(
            req.Text,
            [new Entity("role", "поставщик", EntityProvenance.UserExplicit)],
            [],
            []);

        await sut.RunAsync(@case, "я поставщик, как подать заявку?", CancellationToken.None);

        var forwarded = Assert.Single(_knowledge.LastRetrieveRequest!.Entities);
        Assert.Equal("role", forwarded.Type);
        Assert.Equal("поставщик", forwarded.Value);
        Assert.Equal(EntityProvenance.UserExplicit, forwarded.Provenance);
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

        var push = Assert.IsType<QualityTurnPush>(Assert.Single(_outbox.Enqueued).Payload);
        Assert.Equal(QualityOutboxMessages.Turn, _outbox.Enqueued[0].MessageType);
        Assert.Equal("Answer", push.Decision);
        Assert.Equal("Ответ.", push.AnswerMarkdown);
        Assert.Equal(["frag-1"], push.EvidenceFragmentIds);
        Assert.Equal("snapshot-1", push.SnapshotId);
        Assert.NotNull(push.StageTimings.UnderstandMs);
        Assert.NotNull(push.StageTimings.RetrieveMs);
        Assert.NotNull(push.StageTimings.DraftMs);
        Assert.NotNull(push.StageTimings.VerifyMs);
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

    [Theory]
    [InlineData(KnowledgeFailureCategory.Timeout)]
    [InlineData(KnowledgeFailureCategory.Unavailable)]
    [InlineData(KnowledgeFailureCategory.InvalidResponse)]
    [InlineData(KnowledgeFailureCategory.ModelError)]
    public async Task KnowledgeFailureProducesTechnicalErrorNotNoAnswer(KnowledgeFailureCategory category)
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.FailAt = new KnowledgeFailureException(category, "boom");
        _knowledge.FailingStage = nameof(FakeKnowledgeService.RetrieveAsync);

        var outcome = await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        Assert.Equal(Decision.TechnicalError, outcome.Decision);
        Assert.Equal(category, outcome.FailureCategory);
        Assert.Equal(TurnStatus.Failed, @case.ActiveTurn!.Status);
        Assert.Contains(_events.Published, e => e.Event.Type == "TECHNICAL_ERROR");

        var push = Assert.IsType<QualityTurnPush>(Assert.Single(_outbox.Enqueued).Payload);
        Assert.Equal("TechnicalError", push.Decision);
        Assert.Equal(category.ToString(), push.ErrorCategory);
        Assert.NotNull(push.StageTimings.UnderstandMs);
        Assert.Null(push.StageTimings.RetrieveMs);
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
