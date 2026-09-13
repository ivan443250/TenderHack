using TenderHack.Application.Knowledge;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Ports;
using TenderHack.Application.Tests.Fakes;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Common;
using Xunit;

namespace TenderHack.Application.Tests;

public sealed class TurnOrchestratorTests
{
    private readonly FakeKnowledgeService _knowledge = new();
    private readonly FakeModerationRuleEngine _moderation = new();
    private readonly FakeTurnEventStream _events = new();
    private readonly FakeOutbox _outbox = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeNotificationSink _notifications = new();

    private TurnOrchestrator CreateSut(int closeAfterWarnings = 1) =>
        new(_knowledge, _moderation, _events, _outbox, _unitOfWork, new ModerationOptions(closeAfterWarnings), TimeProvider.System,
            new CaseCompletionPublisher(_events, _notifications, _outbox),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TurnOrchestrator>.Instance);

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
        var warning = _events.Published.First(e => e.Event.Type == "MODERATION_WARNING");
        Assert.Equal("PROFANITY_001", warning.Event.Payload["rule_id"]);
        Assert.Equal("v1", warning.Event.Payload["rule_version"]);
        Assert.Equal(1, warning.Event.Payload["close_after_warnings"]);
        var message = Assert.IsType<string>(warning.Event.Payload["message"]);
        Assert.Contains("Предупреждение 1 из 1", message);
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
        var closed = _events.Published.First(e => e.Event.Type == "CONVERSATION_CLOSED");
        Assert.Equal("PROFANITY_001", closed.Event.Payload["rule_id"]);
        Assert.Equal(1, closed.Event.Payload["close_after_warnings"]);
        Assert.IsType<string>(closed.Event.Payload["message"]);

        // B5 (docs/plans/active/2026-09-demo-readiness.md): moderation close must still complete the
        // case like any other terminal path (architecture.md §5.8) — exactly one CASE_COMPLETED with
        // completion_reason MODERATION, and never a feedback prompt (product-spec.md §14).
        var completed = Assert.Single(_events.Published, e => e.Event.Type == "CASE_COMPLETED");
        Assert.Equal("MODERATION", completed.Event.Payload["completion_reason"]);
        Assert.DoesNotContain(_events.Published, e => e.Event.Type == "FEEDBACK_REQUESTED");
        Assert.Contains(_notifications.Enqueued, n => n.Type == "CASE_COMPLETED");
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
    public async Task ShortExplicitHumanRequestVariantOffersHandoffWithoutCallingKnowledge()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.FailAt = new InvalidOperationException("scope gate should stop before Knowledge");
        _knowledge.FailingStage = nameof(FakeKnowledgeService.UnderstandAsync);

        var outcome = await sut.RunAsync(@case, "позови специалиста", CancellationToken.None);

        Assert.Equal(Decision.HandoffOffer, outcome.Decision);
        Assert.DoesNotContain(_events.Published, e => e.Event.Type == "TURN_STAGE");
        Assert.Contains(_events.Published, e => e.Event.Type == "HANDOFF_OFFER");
    }

    [Theory]
    [InlineData("привет")]
    [InlineData("спасибо")]
    [InlineData("как приготовить борщ?")]
    [InlineData("как подключить Kafka к Порталу поставщиков?")]
    public async Task ObviousOutOfScopeMessageGetsGuidanceWithoutKnowledgeOrHandoff(string text)
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.FailAt = new InvalidOperationException("scope gate should stop before Knowledge");
        _knowledge.FailingStage = nameof(FakeKnowledgeService.UnderstandAsync);

        var outcome = await sut.RunAsync(@case, text, CancellationToken.None);

        Assert.Equal(Decision.OutOfScope, outcome.Decision);
        Assert.DoesNotContain(_events.Published, e => e.Event.Type == "TURN_STAGE");
        Assert.DoesNotContain(_events.Published, e => e.Event.Type == "HANDOFF_OFFER");
        var scopeEvent = Assert.Single(_events.Published, e => e.Event.Type == "OUT_OF_SCOPE");
        Assert.NotEmpty(Assert.IsType<string>(scopeEvent.Event.Payload["message"]));
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
    public async Task AnswerApplicabilityCarriesMappedQuestionsAlongsideRawMissingConditions()
    {
        // F1.3 (docs/plans/active/2026-09-product-enhancements.md): the Applicability Card's own
        // "Нужно уточнить" block reuses B1's slot→question mapping — same list, same order.
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], ["role"], []);

        await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        var answer = Assert.Single(_events.Published, e => e.Event.Type == "AI_ANSWER");
        var applicability = answer.Event.Payload["applicability"]!;
        var questions = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            applicability.GetType().GetProperty("questions")!.GetValue(applicability)).Cast<string>().ToArray();
        Assert.Equal(["Вы работаете на Портале как поставщик или как заказчик?"], questions);
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
    public async Task KnowledgeUnavailableRiskFlagProducesTechnicalErrorNotNoConfirmedAnswer()
    {
        // knowledge signals an unreachable repository as a plain 200 INSUFFICIENT answerability
        // response carrying this flag (not a 5xx) — infrastructure failure must never surface as
        // "no confirmed answer" (product-spec.md §9).
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Insufficient, [], [], ["KNOWLEDGE_UNAVAILABLE"]);

        var outcome = await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        Assert.Equal(Decision.TechnicalError, outcome.Decision);
        Assert.Equal(KnowledgeFailureCategory.Unavailable, outcome.FailureCategory);
        Assert.DoesNotContain(_events.Published, e => e.Event.Type == "NO_CONFIRMED_ANSWER");
        Assert.DoesNotContain(_events.Published, e => e.Event.Type == "HANDOFF_OFFER");
        Assert.Contains(_events.Published, e => e.Event.Type == "TECHNICAL_ERROR");
    }

    [Fact]
    public async Task InsufficientEvidenceWithAnExactCodeExpandsRetrievalOnceBeforeHandoff()
    {
        // product-spec.md §8 п.9: "максимум одно дополнительное расширение поиска" — the first
        // retrieve was narrowed by an exact code that turned up nothing applicable; one retry
        // without it before giving up.
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.UnderstandOverride = req => new UnderstandResult(req.Text, [], ["РДИК_9999"], []);
        _knowledge.AnswerabilitySequence = new Queue<AnswerabilityResult>(
        [
            new AnswerabilityResult(EvidenceSufficiency.Insufficient, [], [], []),
            new AnswerabilityResult(EvidenceSufficiency.Insufficient, [], [], []),
        ]);

        var outcome = await sut.RunAsync(@case, "код РДИК_9999 не работает", CancellationToken.None);

        Assert.Equal(Decision.HandoffOffer, outcome.Decision);
        Assert.Equal(2, _knowledge.RetrieveRequests.Count);
        Assert.Equal(["РДИК_9999"], _knowledge.RetrieveRequests[0].ExactCodes);
        Assert.Empty(_knowledge.RetrieveRequests[1].ExactCodes);
        var handoffEvent = _events.Published.Last(e => e.Event.Type == "HANDOFF_OFFER");
        var reasonCodes = Assert.IsAssignableFrom<IReadOnlyList<string>>(handoffEvent.Event.Payload["reason_codes"]);
        Assert.Contains("EXPANDED_RETRY", reasonCodes);
    }

    [Fact]
    public async Task ExpandedRetrievalFindingSomethingApplicablePublishesTheAnswerInstead()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.UnderstandOverride = req => new UnderstandResult(req.Text, [], ["РДИК_9999"], []);
        _knowledge.AnswerabilitySequence = new Queue<AnswerabilityResult>(
        [
            new AnswerabilityResult(EvidenceSufficiency.Insufficient, [], [], []),
            new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []),
        ]);

        var outcome = await sut.RunAsync(@case, "код РДИК_9999 не работает", CancellationToken.None);

        Assert.Equal(Decision.Answer, outcome.Decision);
        Assert.Equal(2, _knowledge.RetrieveRequests.Count);
    }

    [Fact]
    public async Task InsufficientEvidenceWithoutAnExactCodeNeverExpandsRetrieval()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Insufficient, [], [], []);

        var outcome = await sut.RunAsync(@case, "просто вопрос без кода", CancellationToken.None);

        Assert.Equal(Decision.HandoffOffer, outcome.Decision);
        Assert.Single(_knowledge.RetrieveRequests);
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
        _knowledge.UnderstandOverride = req => new UnderstandResult(
            req.Text, [new Entity("role", "поставщик", EntityProvenance.UserExplicit)], [], []);

        var outcome = await sut.RunAsync(@case, "как подать заявку?", CancellationToken.None);

        Assert.Equal(Decision.Answer, outcome.Decision);
        Assert.Equal("Ответ.", outcome.AnswerMarkdown);
        var source = Assert.Single(outcome.Sources!);
        Assert.Equal("frag-1", source.FragmentId);
        Assert.Equal("doc-1", source.DocumentId);
        Assert.Equal(1, source.Page);
        // Title falls back to DocumentId when knowledge did not supply one (Candidate.title is additive/optional).
        Assert.Equal("doc-1", source.Title);
        Assert.Equal(ResolutionStatus.Unknown, @case.ResolutionStatus);

        // product-experience.md §5 Applicability Card: the same facts that passed the Answerability
        // Gate, not a re-derived confidence score.
        var answerEvent = _events.Published.Single(e => e.Event.Type == "AI_ANSWER");
        var applicability = answerEvent.Event.Payload["applicability"]!;
        var entitiesProperty = applicability.GetType().GetProperty("entities")!.GetValue(applicability);
        var entities = Assert.IsAssignableFrom<System.Collections.IEnumerable>(entitiesProperty).Cast<object>().ToArray();
        var roleEntity = Assert.Single(entities);
        Assert.Equal("поставщик", roleEntity.GetType().GetProperty("value")!.GetValue(roleEntity));
        Assert.Equal("user_explicit", roleEntity.GetType().GetProperty("provenance")!.GetValue(roleEntity));
        var applicabilityFragmentIds = applicability.GetType().GetProperty("evidence_fragment_ids")!.GetValue(applicability);
        Assert.Equal(new[] { "frag-1" }, applicabilityFragmentIds);
        var answer = Assert.Single(_events.Published, e => e.Event.Type == "AI_ANSWER");
        // web-api-v0.md §4.3: sources are objects with fragment_id/document_id/page/anchor/title, not bare ids.
        var publishedSources = Assert.IsAssignableFrom<System.Collections.IEnumerable>(answer.Event.Payload["sources"]).Cast<object>().ToArray();
        var publishedSource = Assert.Single(publishedSources);
        Assert.Equal("frag-1", publishedSource.GetType().GetProperty("fragment_id")!.GetValue(publishedSource));
        Assert.Equal("doc-1", publishedSource.GetType().GetProperty("title")!.GetValue(publishedSource));
        // architecture.md §8 "Knowledge versioning": every user-facing answer records snapshot/model/retrieval config.
        Assert.Equal("snapshot-1", answer.Event.Payload["snapshot_id"]);
        Assert.Equal("stub-v0", answer.Event.Payload["model_version"]);
        Assert.Equal("stub-v0", answer.Event.Payload["retrieval_config_version"]);

        // F1.3 (docs/plans/active/2026-09-product-enhancements.md): applicability.questions is
        // additive and empty here since nothing was missing for a plain ANSWER.
        var applicabilityForAnswer = answer.Event.Payload["applicability"]!;
        var questionsForAnswer = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            applicabilityForAnswer.GetType().GetProperty("questions")!.GetValue(applicabilityForAnswer));
        Assert.Empty(questionsForAnswer.Cast<object>());

        var push = Assert.IsType<QualityTurnPush>(Assert.Single(_outbox.Enqueued).Payload);
        Assert.Equal(QualityOutboxMessages.Turn, _outbox.Enqueued[0].MessageType);
        // web-api-v0.md §4/knowledge-v0.md decision vocabulary is UPPER_SNAKE_CASE on the wire; this
        // must never leak the C# PascalCase member name.
        Assert.Equal("ANSWER", push.Decision);
        Assert.Equal("Ответ.", push.AnswerMarkdown);
        Assert.Equal(["frag-1"], push.EvidenceFragmentIds);
        Assert.Equal("snapshot-1", push.SnapshotId);
        Assert.Equal("stub-v0", push.ModelVersion);
        Assert.Equal("stub-v0", push.RetrievalConfigVersion);
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
        // product-spec.md §12: one extractive rewrite attempt before giving up — both draft calls happened.
        Assert.Equal(2, _knowledge.DraftRequests.Count);
        Assert.Null(_knowledge.DraftRequests[0].Tone);
        Assert.Equal("extractive", _knowledge.DraftRequests[1].Tone);
        // Distinct from a plain "no evidence" handoff, for Knowledge Gap Radar (product-experience.md §8).
        var handoffEvent = _events.Published.Last(e => e.Event.Type == "NO_CONFIRMED_ANSWER");
        Assert.Equal("VERIFICATION_FAILED", handoffEvent.Event.Payload["reason"]);
    }

    [Fact]
    public async Task ExtractiveRewriteSucceedingPublishesTheAnswerInstead()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.VerifySequence = new Queue<VerifyResult>(
        [
            new VerifyResult([new ClaimVerification("claim-1", Supported: false, [])]),
            new VerifyResult([new ClaimVerification("claim-1", Supported: true, ["frag-1"])]),
        ]);

        var outcome = await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        Assert.Equal(Decision.Answer, outcome.Decision);
        Assert.Equal(2, _knowledge.DraftRequests.Count);
        Assert.Equal("extractive", _knowledge.DraftRequests[1].Tone);
        Assert.DoesNotContain(_events.Published, e => e.Event.Type == "NO_CONFIRMED_ANSWER");
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
        Assert.Equal(1, @case.TurnContext.ConsecutiveClarifications);
    }

    [Fact]
    public async Task ClarificationPublishesHumanReadableQuestionsAlongsideRawSlotIds()
    {
        // B1 (docs/plans/active/2026-09-demo-readiness.md): the UI must not render raw slot ids like
        // "role"/"unknown_slot" as the question text — `questions` is additive next to
        // `missing_conditions`, and an unmapped slot still gets a usable fallback question.
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.ConditionDependent, [], ["role", "unknown_slot"], []);

        await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        var clarification = Assert.Single(_events.Published, e => e.Event.Type == "CLARIFICATION");
        Assert.Equal(new[] { "role", "unknown_slot" }, clarification.Event.Payload["missing_conditions"]);
        var questions = Assert.IsAssignableFrom<IReadOnlyList<string>>(clarification.Event.Payload["questions"]);
        Assert.Equal("Вы работаете на Портале как поставщик или как заказчик?", questions[0]);
        Assert.Equal("Уточните детали ситуации, чтобы подобрать точную инструкцию.", questions[1]);
    }

    [Fact]
    public async Task ClarificationMapsAllKnownConditionSlotsToHumanQuestions()
    {
        var sut = CreateSut();
        var @case = NewCase();
        var slots = new[]
        {
            "role", "provider", "process", "document_type", "document_status", "status",
            "attempted_action", "duration", "already_tried", "error_code", "category_id", "model", "source_state",
        };
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.ConditionDependent, [], slots, []);

        await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        var clarification = Assert.Single(_events.Published, e => e.Event.Type == "CLARIFICATION");
        var questions = Assert.IsAssignableFrom<IReadOnlyList<string>>(clarification.Event.Payload["questions"]);
        Assert.Equal(slots.Length, questions.Count);
        Assert.All(questions, question =>
        {
            Assert.NotEmpty(question);
            Assert.DoesNotContain("_", question);
        });
    }

    [Fact]
    public async Task AThirdConsecutiveClarificationEscalatesToHandoffInstead()
    {
        // product-spec.md §11: "не более двух последовательных clarifications в одном нерешенном сценарии".
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.ConditionDependent, [], ["сумма контракта"], []);

        var first = await sut.RunAsync(@case, "вопрос", CancellationToken.None);
        var second = await sut.RunAsync(@case, "уточнение 1", CancellationToken.None);
        var third = await sut.RunAsync(@case, "уточнение 2", CancellationToken.None);

        Assert.Equal(Decision.Clarify, first.Decision);
        Assert.Equal(Decision.Clarify, second.Decision);
        Assert.Equal(Decision.HandoffOffer, third.Decision);
        var handoffEvent = _events.Published.Last(e => e.Event.Type == "HANDOFF_OFFER");
        var reasonCodes = Assert.IsAssignableFrom<IReadOnlyList<string>>(handoffEvent.Event.Payload["reason_codes"]);
        Assert.Contains("CLARIFICATION_LIMIT", reasonCodes);
        // B4: a multi-word enum member (ServiceNeed.AccountOrStateCheck) is the case that a bare
        // .ToString() previously got wrong (single-word members like SupportLine.L1 masked the bug).
        Assert.Equal("ACCOUNT_OR_STATE_CHECK", handoffEvent.Event.Payload["service_need"]);
        Assert.Equal("L1", handoffEvent.Event.Payload["recommended_line"]);
        var push = Assert.IsType<QualityTurnPush>(_outbox.Enqueued.Last().Payload);
        Assert.Equal("HANDOFF_OFFER", push.Decision);
        Assert.Equal("ACCOUNT_OR_STATE_CHECK", push.ServiceNeed);
        Assert.Equal("L1", push.RecommendedLine);
    }

    [Fact]
    public async Task AnsweringResetsTheClarificationCounterForTheNextUnrelatedQuestion()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.ConditionDependent, [], ["сумма контракта"], []);
        await sut.RunAsync(@case, "вопрос 1", CancellationToken.None);
        await sut.RunAsync(@case, "уточнение", CancellationToken.None);
        Assert.Equal(2, @case.TurnContext.ConsecutiveClarifications);

        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
        var answered = await sut.RunAsync(@case, "новый вопрос", CancellationToken.None);

        Assert.Equal(Decision.Answer, answered.Decision);
        Assert.Equal(0, @case.TurnContext.ConsecutiveClarifications);
    }

    [Fact]
    public async Task DecliningTheOutstandingClarificationGoesStraightToHandoffWithoutRetrieval()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.ConditionDependent, [], ["сумма контракта"], []);
        await sut.RunAsync(@case, "вопрос", CancellationToken.None);
        var retrieveRequestAfterFirstTurn = _knowledge.LastRetrieveRequest;

        var outcome = await sut.RunAsync(@case, "не знаю", CancellationToken.None);

        Assert.Equal(Decision.HandoffOffer, outcome.Decision);
        // Same object reference as after the first turn — retrieve was not called again for the decline.
        Assert.Same(retrieveRequestAfterFirstTurn, _knowledge.LastRetrieveRequest);
        Assert.Contains("сумма контракта", @case.TurnContext.DeclinedMissingConditions);
        var handoffEvent = _events.Published.Last(e => e.Event.Type == "HANDOFF_OFFER");
        var reasonCodes = Assert.IsAssignableFrom<IReadOnlyList<string>>(handoffEvent.Event.Payload["reason_codes"]);
        Assert.Contains("CLARIFICATION_DECLINED", reasonCodes);
    }

    [Fact]
    public async Task AKnownSlotFromAnEarlierTurnIsForwardedToRetrieveOnTheNextTurnWithoutBeingRestated()
    {
        // product-spec.md §7: "не спрашивать уже известное" — a slot understood on turn 1 must still
        // reach retrieve on turn 2 even though turn 2's own message never repeats it.
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.UnderstandOverride = req => new UnderstandResult(
            req.Text, [new Entity("role", "поставщик", EntityProvenance.UserExplicit)], [], []);
        await sut.RunAsync(@case, "я поставщик", CancellationToken.None);

        _knowledge.UnderstandOverride = req => new UnderstandResult(req.Text, [], [], []);
        await sut.RunAsync(@case, "как подать заявку?", CancellationToken.None);

        var forwarded = Assert.Single(_knowledge.LastRetrieveRequest!.Entities);
        Assert.Equal("role", forwarded.Type);
        Assert.Equal("поставщик", forwarded.Value);
    }

    [Fact]
    public async Task PriorTurnSummaryCarriesTheLastQuestionAndKnownSlotsIntoUnderstand()
    {
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.ConditionDependent, [], ["роль пользователя"], []);
        _knowledge.UnderstandOverride = req => new UnderstandResult(
            req.Text, [new Entity("document_type", "УПД", EntityProvenance.UserExplicit)], [], []);
        await sut.RunAsync(@case, "что делать с УПД?", CancellationToken.None);

        UnderstandRequest? secondRequest = null;
        _knowledge.UnderstandOverride = req =>
        {
            secondRequest = req;
            return new UnderstandResult(req.Text, [], [], []);
        };
        await sut.RunAsync(@case, "поставщик", CancellationToken.None);

        Assert.NotNull(secondRequest!.PriorTurnSummary);
        Assert.Contains("что делать с УПД?", secondRequest.PriorTurnSummary);
        Assert.Contains("document_type=УПД", secondRequest.PriorTurnSummary);
        Assert.Contains("роль пользователя", secondRequest.PriorTurnSummary);
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
        var technicalError = Assert.Single(_events.Published, e => e.Event.Type == "TECHNICAL_ERROR");
        Assert.Equal(category.ToWire(), technicalError.Event.Payload["category"]);

        var push = Assert.IsType<QualityTurnPush>(Assert.Single(_outbox.Enqueued).Payload);
        Assert.Equal("TECHNICAL_ERROR", push.Decision);
        Assert.Equal(category.ToWire(), push.ErrorCategory);
        Assert.NotNull(push.StageTimings.UnderstandMs);
        Assert.Null(push.StageTimings.RetrieveMs);
    }

    [Fact]
    public async Task TurnIsPersistedBeforeTheFirstStageRuns()
    {
        // Regression: the turn used to be committed only with the final decision, so any crash
        // between StartTurn and that commit left USER_MESSAGE/TURN_STAGE events pointing at a turn
        // row that never existed — and nothing for `stale-turn cleanup` to recover.
        var sut = CreateSut();
        var @case = NewCase();
        var savesWhenUnderstandRan = -1;
        _knowledge.UnderstandOverride = req =>
        {
            savesWhenUnderstandRan = _unitOfWork.SaveChangesCallCount;
            return new UnderstandResult(req.Text, [], [], []);
        };

        await sut.RunAsync(@case, "вопрос", CancellationToken.None);

        Assert.Equal(1, savesWhenUnderstandRan);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount); // the final decision is the caller's commit
    }

    [Fact]
    public async Task UnclassifiedFailureFailsThePersistedTurnAndStillPropagates()
    {
        // A defect or infrastructure error is not a KnowledgeFailureException: it must surface to the
        // caller (500), but the QUEUED row from the first commit must not be left hanging until TTL.
        var sut = CreateSut();
        var @case = NewCase();
        _knowledge.FailAt = new InvalidOperationException("bug");
        _knowledge.FailingStage = nameof(FakeKnowledgeService.RetrieveAsync);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(@case, "вопрос", CancellationToken.None));

        Assert.Equal(TurnStatus.Failed, @case.ActiveTurn!.Status);
        var error = Assert.Single(_events.Published, e => e.Event.Type == "TECHNICAL_ERROR");
        Assert.Equal(TurnOrchestrator.InternalErrorCategory, error.Event.Payload["category"]);
        Assert.Equal(2, _unitOfWork.SaveChangesCallCount); // input commit + best-effort failure commit
    }

    [Fact]
    public async Task AmbiguousMatchForwardsTheRuleEngineSpanToKnowledge()
    {
        // Regression: the moderation_context request used to carry a fabricated 0..len span.
        var sut = CreateSut();
        var @case = NewCase();
        _moderation.NextResult = new ModerationRuleMatch("PROFANITY_A01", "v1", "дура", 7, 11, RequiresContextCheck: true);

        await sut.RunAsync(@case, "ну ты и дура", CancellationToken.None);

        Assert.Equal(7, _knowledge.LastModerationContextRequest!.Start);
        Assert.Equal(11, _knowledge.LastModerationContextRequest.End);
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
