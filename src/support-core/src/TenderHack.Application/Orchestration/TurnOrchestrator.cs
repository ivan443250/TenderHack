using TenderHack.Application.Knowledge;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.Orchestration;

/// <summary>
/// Per-turn state machine (architecture.md §5.2): persist → moderate → understand → retrieve →
/// answerability → draft/verify when evidence allows → decide → persist. Every stage result is
/// published as a `case_events` row before the next stage starts.
/// </summary>
public sealed class TurnOrchestrator(
    IKnowledgeService knowledge,
    IModerationRuleEngine moderationRules,
    ITurnEventStream events,
    ModerationOptions moderationOptions,
    TimeProvider clock)
{
    public async Task<TurnOutcome> RunAsync(Case @case, string messageText, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var turn = @case.StartTurn(now);

        await PublishAsync(@case.Id, turn.Id, turn.Revision, "USER_MESSAGE", now,
            new Dictionary<string, object?> { ["text"] = messageText }, ct);

        var ruleMatch = moderationRules.Evaluate(messageText);
        if (ruleMatch is { Confirmed: true })
        {
            return await HandleModerationViolationAsync(@case, turn, now, ct);
        }

        var context = new KnowledgeRequestContext(Guid.NewGuid(), @case.Id, turn.Id);

        try
        {
            var understand = await knowledge.UnderstandAsync(new UnderstandRequest(messageText, PriorTurnSummary: null), context, ct);
            await PublishStageAsync(@case.Id, turn, "Проверяем запрос", now, ct);

            var retrieve = await knowledge.RetrieveAsync(
                new RetrieveRequest(understand.NormalizedText, understand.Entities, understand.ExactCodes, Corpus.Normative, SnapshotId: null),
                context, ct);
            await PublishStageAsync(@case.Id, turn, "Ищем применимые инструкции", now, ct);

            var candidateFragmentIds = retrieve.Candidates.Select(c => c.FragmentId).ToArray();
            var answerability = await knowledge.AssessAnswerabilityAsync(
                new AnswerabilityRequest(understand.NormalizedText, retrieve.SnapshotId, candidateFragmentIds),
                context, ct);
            await PublishStageAsync(@case.Id, turn, "Проверяем условия", now, ct);

            return await DecideFromAnswerabilityAsync(@case, turn, understand, retrieve, answerability, context, now, ct);
        }
        catch (KnowledgeFailureException ex)
        {
            @case.TryFailTurn(turn.Id, turn.Revision);
            await PublishAsync(@case.Id, turn.Id, turn.Revision, "TECHNICAL_ERROR", now,
                new Dictionary<string, object?> { ["category"] = ex.Category.ToString() }, ct);
            return TurnOutcome.TechnicalError(turn.Id, turn.Revision, @case.ModerationWarningCount, ex.Category);
        }
    }

    private async Task<TurnOutcome> HandleModerationViolationAsync(Case @case, Turn turn, DateTimeOffset now, CancellationToken ct)
    {
        var decision = @case.RecordModerationViolation(moderationOptions.CloseAfterWarnings, now);

        await PublishAsync(@case.Id, turn.Id, turn.Revision, decision == Decision.ModerationClose ? "CONVERSATION_CLOSED" : "MODERATION_WARNING",
            now, new Dictionary<string, object?> { ["moderation_warning_count"] = @case.ModerationWarningCount }, ct);

        return TurnOutcome.Moderation(turn.Id, turn.Revision, decision, @case.ModerationWarningCount);
    }

    private async Task<TurnOutcome> DecideFromAnswerabilityAsync(
        Case @case,
        Turn turn,
        UnderstandResult understand,
        RetrieveResult retrieve,
        AnswerabilityResult answerability,
        KnowledgeRequestContext context,
        DateTimeOffset now,
        CancellationToken ct)
    {
        switch (answerability.EvidenceSufficiency)
        {
            case EvidenceSufficiency.ConditionDependent:
                @case.TryPublishDecision(turn.Id, turn.Revision, Decision.Clarify);
                await PublishAsync(@case.Id, turn.Id, turn.Revision, "CLARIFICATION", now,
                    new Dictionary<string, object?> { ["missing_conditions"] = answerability.MissingConditions }, ct);
                return TurnOutcome.Clarify(turn.Id, turn.Revision, @case.ModerationWarningCount, answerability.MissingConditions);

            case EvidenceSufficiency.Insufficient:
                @case.TryPublishDecision(turn.Id, turn.Revision, Decision.HandoffOffer);
                await PublishAsync(@case.Id, turn.Id, turn.Revision, "NO_CONFIRMED_ANSWER", now,
                    new Dictionary<string, object?>(), ct);
                return TurnOutcome.HandoffOffer(turn.Id, turn.Revision, @case.ModerationWarningCount);

            case EvidenceSufficiency.Sufficient:
                var draft = await knowledge.DraftAsync(
                    new DraftRequest(understand.NormalizedText, retrieve.SnapshotId, answerability.EvidenceFragmentIds),
                    context, ct);
                var verify = await knowledge.VerifyAsync(new VerifyRequest(draft.Claims, retrieve.SnapshotId), context, ct);
                await PublishStageAsync(@case.Id, turn, "Готовим ответ / передачу", now, ct);

                if (verify.Results.Count > 0 && verify.Results.All(r => r.Supported))
                {
                    @case.TryPublishDecision(turn.Id, turn.Revision, Decision.Answer);
                    await PublishAsync(@case.Id, turn.Id, turn.Revision, "AI_ANSWER", now,
                        new Dictionary<string, object?> { ["markdown"] = draft.Markdown }, ct);
                    return TurnOutcome.Answered(turn.Id, turn.Revision, @case.ModerationWarningCount, draft.Markdown, answerability.EvidenceFragmentIds);
                }

                // A claim failed verification: do not publish an unsupported answer — offer human support instead.
                @case.TryPublishDecision(turn.Id, turn.Revision, Decision.HandoffOffer);
                await PublishAsync(@case.Id, turn.Id, turn.Revision, "NO_CONFIRMED_ANSWER", now,
                    new Dictionary<string, object?>(), ct);
                return TurnOutcome.HandoffOffer(turn.Id, turn.Revision, @case.ModerationWarningCount);

            default:
                throw new ArgumentOutOfRangeException(nameof(answerability), answerability.EvidenceSufficiency, null);
        }
    }

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
