using TenderHack.Application.Knowledge;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Routing;

namespace TenderHack.Application.Orchestration;

/// <summary>
/// Per-turn state machine (architecture.md §5.2): persist → moderate → direct human-request check →
/// understand → retrieve → answerability → draft/verify when evidence allows → decide → persist.
/// Every stage result is published as a `case_events` row before the next stage starts. On every
/// exit it also enqueues a `quality.turn` outbox push (knowledge-v0.md §10) for analytics.
/// </summary>
public sealed class TurnOrchestrator(
    IKnowledgeService knowledge,
    IModerationRuleEngine moderationRules,
    ITurnEventStream events,
    IOutbox outbox,
    ModerationOptions moderationOptions,
    TimeProvider clock)
{
    public async Task<TurnOutcome> RunAsync(Case @case, string messageText, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var turn = @case.StartTurn(now);
        var context = new KnowledgeRequestContext(Guid.NewGuid(), @case.Id, turn.Id);
        var timings = new StageTimingsAccumulator();

        await PublishAsync(@case.Id, turn.Id, turn.Revision, "USER_MESSAGE", now,
            new Dictionary<string, object?> { ["text"] = messageText }, ct);

        try
        {
            if (await IsConfirmedViolationAsync(messageText, context, ct))
            {
                return await HandleModerationViolationAsync(@case, turn, messageText, timings, now, ct);
            }

            if (DirectHumanRequestDetector.IsExplicitRequest(messageText))
            {
                return await OfferHandoffAsync(@case, turn, messageText, timings, now,
                    evidenceInsufficient: true, conditionDependent: false, [], explicitHumanRequest: true, ct);
            }

            var understand = await StageTimingsAccumulator.TimeAsync(
                () => knowledge.UnderstandAsync(new UnderstandRequest(messageText, PriorTurnSummary: null), context, ct),
                ms => timings.UnderstandMs = ms);
            await PublishStageAsync(@case.Id, turn, "Проверяем запрос", now, ct);

            var retrieve = await StageTimingsAccumulator.TimeAsync(
                () => knowledge.RetrieveAsync(
                    new RetrieveRequest(understand.NormalizedText, understand.Entities, understand.ExactCodes, Corpus.Normative, SnapshotId: null),
                    context, ct),
                ms => timings.RetrieveMs = ms);
            await PublishStageAsync(@case.Id, turn, "Ищем применимые инструкции", now, ct);

            var candidateFragmentIds = retrieve.Candidates.Select(c => c.FragmentId).ToArray();
            var answerability = await knowledge.AssessAnswerabilityAsync(
                new AnswerabilityRequest(understand.NormalizedText, retrieve.SnapshotId, candidateFragmentIds),
                context, ct);
            await PublishStageAsync(@case.Id, turn, "Проверяем условия", now, ct);

            return await DecideFromAnswerabilityAsync(@case, turn, messageText, understand, retrieve, answerability, context, timings, now, ct);
        }
        catch (KnowledgeFailureException ex)
        {
            @case.TryFailTurn(turn.Id, turn.Revision);
            await PublishAsync(@case.Id, turn.Id, turn.Revision, "TECHNICAL_ERROR", now,
                new Dictionary<string, object?> { ["category"] = ex.Category.ToString() }, ct);
            EnqueueQualityTurnPush(@case, turn, messageText, Decision.TechnicalError, timings,
                answerMarkdown: null, evidenceFragmentIds: null, snapshotId: null, routing: null, errorCategory: ex.Category.ToString());
            return TurnOutcome.TechnicalError(turn.Id, turn.Revision, @case.ModerationWarningCount, ex.Category);
        }
    }

    /// <summary>
    /// A confirmed-list rule hit is a confirmed violation outright. An ambiguous hit is escalated to
    /// `knowledge.AssessModerationContextAsync`; only `OFFENSIVE` confirms it — `UNCERTAIN`/`NOT_OFFENSIVE`
    /// let the turn continue normally (product-spec.md §14 step 4).
    /// </summary>
    private async Task<bool> IsConfirmedViolationAsync(string messageText, KnowledgeRequestContext context, CancellationToken ct)
    {
        var match = moderationRules.Evaluate(messageText);
        if (match is null)
        {
            return false;
        }

        if (!match.RequiresContextCheck)
        {
            return true;
        }

        var assessment = await knowledge.AssessModerationContextAsync(
            new ModerationContextRequest(messageText, match.RuleId, match.RuleVersion, match.MatchedTerm), context, ct);
        return assessment.Ambiguity == ModerationAmbiguity.Offensive;
    }

    private async Task<TurnOutcome> HandleModerationViolationAsync(
        Case @case, Turn turn, string messageText, StageTimingsAccumulator timings, DateTimeOffset now, CancellationToken ct)
    {
        var decision = @case.RecordModerationViolation(moderationOptions.CloseAfterWarnings, now);

        await PublishAsync(@case.Id, turn.Id, turn.Revision, decision == Decision.ModerationClose ? "CONVERSATION_CLOSED" : "MODERATION_WARNING",
            now, new Dictionary<string, object?> { ["moderation_warning_count"] = @case.ModerationWarningCount }, ct);
        EnqueueQualityTurnPush(@case, turn, messageText, decision, timings,
            answerMarkdown: null, evidenceFragmentIds: null, snapshotId: null, routing: null, errorCategory: null);

        return TurnOutcome.Moderation(turn.Id, turn.Revision, decision, @case.ModerationWarningCount);
    }

    private async Task<TurnOutcome> DecideFromAnswerabilityAsync(
        Case @case,
        Turn turn,
        string messageText,
        UnderstandResult understand,
        RetrieveResult retrieve,
        AnswerabilityResult answerability,
        KnowledgeRequestContext context,
        StageTimingsAccumulator timings,
        DateTimeOffset now,
        CancellationToken ct)
    {
        switch (answerability.EvidenceSufficiency)
        {
            case EvidenceSufficiency.ConditionDependent:
                @case.TryPublishDecision(turn.Id, turn.Revision, Decision.Clarify);
                await PublishAsync(@case.Id, turn.Id, turn.Revision, "CLARIFICATION", now,
                    new Dictionary<string, object?> { ["missing_conditions"] = answerability.MissingConditions }, ct);
                EnqueueQualityTurnPush(@case, turn, messageText, Decision.Clarify, timings,
                    answerMarkdown: null, evidenceFragmentIds: null, retrieve.SnapshotId, routing: null, errorCategory: null);
                return TurnOutcome.Clarify(turn.Id, turn.Revision, @case.ModerationWarningCount, answerability.MissingConditions);

            case EvidenceSufficiency.Insufficient:
                return await OfferHandoffAsync(@case, turn, messageText, timings, now,
                    evidenceInsufficient: true, conditionDependent: false, answerability.RiskFlags, explicitHumanRequest: false, ct, retrieve.SnapshotId);

            case EvidenceSufficiency.Sufficient:
                var draft = await StageTimingsAccumulator.TimeAsync(
                    () => knowledge.DraftAsync(
                        new DraftRequest(understand.NormalizedText, retrieve.SnapshotId, answerability.EvidenceFragmentIds),
                        context, ct),
                    ms => timings.DraftMs = ms);
                var verify = await StageTimingsAccumulator.TimeAsync(
                    () => knowledge.VerifyAsync(new VerifyRequest(draft.Claims, retrieve.SnapshotId), context, ct),
                    ms => timings.VerifyMs = ms);
                await PublishStageAsync(@case.Id, turn, "Готовим ответ / передачу", now, ct);

                if (verify.Results.Count == 0 || !verify.Results.All(r => r.Supported))
                {
                    // A claim failed verification: do not publish an unsupported answer — offer human support instead.
                    return await OfferHandoffAsync(@case, turn, messageText, timings, now,
                        evidenceInsufficient: true, conditionDependent: false, answerability.RiskFlags, explicitHumanRequest: false, ct, retrieve.SnapshotId);
                }

                if (answerability.RiskFlags.Count > 0)
                {
                    // A confirmed, verified answer, but the situation/instruction itself still needs
                    // support to act on (product-spec.md §10 ANSWER_AND_HANDOFF).
                    var routing = RoutingPolicy.Evaluate(evidenceInsufficient: false, conditionDependent: false, answerability.RiskFlags);
                    @case.TryPublishDecision(turn.Id, turn.Revision, Decision.AnswerAndHandoff);
                    await PublishAsync(@case.Id, turn.Id, turn.Revision, "AI_ANSWER", now,
                        new Dictionary<string, object?> { ["markdown"] = draft.Markdown }, ct);
                    await PublishHandoffOfferAsync(@case, turn, now, routing, ct);
                    EnqueueQualityTurnPush(@case, turn, messageText, Decision.AnswerAndHandoff, timings,
                        draft.Markdown, answerability.EvidenceFragmentIds, retrieve.SnapshotId, routing, errorCategory: null);
                    return TurnOutcome.AnsweredWithHandoff(turn.Id, turn.Revision, @case.ModerationWarningCount, draft.Markdown, answerability.EvidenceFragmentIds);
                }

                @case.TryPublishDecision(turn.Id, turn.Revision, Decision.Answer);
                await PublishAsync(@case.Id, turn.Id, turn.Revision, "AI_ANSWER", now,
                    new Dictionary<string, object?> { ["markdown"] = draft.Markdown }, ct);
                EnqueueQualityTurnPush(@case, turn, messageText, Decision.Answer, timings,
                    draft.Markdown, answerability.EvidenceFragmentIds, retrieve.SnapshotId, routing: null, errorCategory: null);
                return TurnOutcome.Answered(turn.Id, turn.Revision, @case.ModerationWarningCount, draft.Markdown, answerability.EvidenceFragmentIds);

            default:
                throw new ArgumentOutOfRangeException(nameof(answerability), answerability.EvidenceSufficiency, null);
        }
    }

    private async Task<TurnOutcome> OfferHandoffAsync(
        Case @case,
        Turn turn,
        string messageText,
        StageTimingsAccumulator timings,
        DateTimeOffset now,
        bool evidenceInsufficient,
        bool conditionDependent,
        IReadOnlyList<string> riskFlags,
        bool explicitHumanRequest,
        CancellationToken ct,
        string? snapshotId = null)
    {
        @case.TryPublishDecision(turn.Id, turn.Revision, Decision.HandoffOffer);
        await PublishAsync(@case.Id, turn.Id, turn.Revision, "NO_CONFIRMED_ANSWER", now, new Dictionary<string, object?>(), ct);

        var routing = RoutingPolicy.Evaluate(evidenceInsufficient, conditionDependent, riskFlags, explicitHumanRequest);
        await PublishHandoffOfferAsync(@case, turn, now, routing, ct);
        EnqueueQualityTurnPush(@case, turn, messageText, Decision.HandoffOffer, timings,
            answerMarkdown: null, evidenceFragmentIds: null, snapshotId, routing, errorCategory: null);

        return TurnOutcome.HandoffOffer(turn.Id, turn.Revision, @case.ModerationWarningCount);
    }

    private void EnqueueQualityTurnPush(
        Case @case,
        Turn turn,
        string messageText,
        Decision decision,
        StageTimingsAccumulator timings,
        string? answerMarkdown,
        IReadOnlyList<string>? evidenceFragmentIds,
        string? snapshotId,
        RoutingDecision? routing,
        string? errorCategory) =>
        outbox.Enqueue(QualityOutboxMessages.Turn, new QualityTurnPush(
            @case.Id.ToString(),
            turn.Id.ToString(),
            turn.Revision,
            turn.CreatedAt,
            messageText,
            PriorTurnsSummary: null,
            decision.ToString(),
            routing?.ReasonCodes ?? [],
            answerMarkdown,
            evidenceFragmentIds,
            snapshotId,
            HandoffStatus: @case.Handoff?.Status.ToString(),
            RecommendedLine: routing?.RecommendedLine.ToString(),
            ServiceNeed: routing?.ServiceNeed.ToString(),
            timings.ToPush(),
            errorCategory));

    private Task PublishHandoffOfferAsync(Case @case, Turn turn, DateTimeOffset now, RoutingDecision routing, CancellationToken ct) =>
        PublishAsync(@case.Id, turn.Id, turn.Revision, "HANDOFF_OFFER", now, new Dictionary<string, object?>
        {
            ["service_need"] = routing.ServiceNeed.ToString(),
            ["recommended_line"] = routing.RecommendedLine.ToString(),
            ["dispatch_queue"] = routing.DispatchQueue,
            ["engineering_review_suggested"] = routing.EngineeringReviewSuggested,
            ["reason_codes"] = routing.ReasonCodes,
        }, ct);

    private Task PublishStageAsync(CaseId caseId, Turn turn, string stageLabel, DateTimeOffset now, CancellationToken ct) =>
        PublishAsync(caseId, turn.Id, turn.Revision, "TURN_STAGE", now,
            new Dictionary<string, object?> { ["stage"] = stageLabel }, ct);

    private Task PublishAsync(
        CaseId caseId,
        TurnId turnId,
        int revision,
        string type,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken ct) =>
        events.PublishAsync(caseId, new CaseEvent(type, turnId, revision, occurredAt, payload), ct);
}
