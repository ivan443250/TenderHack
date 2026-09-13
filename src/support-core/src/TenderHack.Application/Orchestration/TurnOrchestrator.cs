using Microsoft.Extensions.Logging;
using TenderHack.Application.Knowledge;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Common;
using TenderHack.Domain.Routing;

namespace TenderHack.Application.Orchestration;

/// <summary>
/// Per-turn state machine (architecture.md §5.2): persist input → moderate → direct human-request
/// check → deterministic scope gate → understand → retrieve → answerability → draft/verify when
/// evidence allows → decide.
/// Every stage result is published as a `case_events` row before the next stage starts. On every
/// exit it also enqueues a `quality.turn` outbox push (knowledge-v0.md §10) for analytics. The
/// final decision is left staged on the unit of work — the caller commits it.
/// </summary>
public sealed class TurnOrchestrator(
    IKnowledgeService knowledge,
    IModerationRuleEngine moderationRules,
    ITurnEventStream events,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    ModerationOptions moderationOptions,
    TimeProvider clock,
    CaseCompletionPublisher completionPublisher,
    ILogger<TurnOrchestrator> logger)
{
    /// <summary>`TECHNICAL_ERROR` category for a failure that is not a classified `knowledge` call failure.</summary>
    public const string InternalErrorCategory = "INTERNAL";

    /// <summary>
    /// `knowledge` signals an unreachable repository as a normal `200 INSUFFICIENT` answerability
    /// response carrying this risk flag (not a `5xx`) — see `src/knowledge/.../api/routes.py`'s
    /// `answerability` handler. Treating that as "no confirmed answer" would violate
    /// "infrastructure failure ≠ в базе нет ответа" (product-spec.md §9, architecture.md §16), so it
    /// is escalated to `TECHNICAL_ERROR` here instead of reaching the normal decision switch. This is
    /// a defensive mapping on the `.NET` side of a `knowledge`-side contract gap tracked in
    /// `docs/plans/active/2026-09-support-core-completion.md` (Open issues) — `knowledge` should
    /// eventually answer `503 MODEL_UNAVAILABLE` for this case instead.
    /// </summary>
    private const string KnowledgeUnavailableRiskFlag = "KNOWLEDGE_UNAVAILABLE";

    public async Task<TurnOutcome> RunAsync(Case @case, string messageText, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var turn = @case.StartTurn(now);

        // Persist input first: the QUEUED turn row must exist before any stage runs, so a crash
        // mid-turn leaves something `stale-turn cleanup` can find and fail, never a timeline of
        // events pointing at a turn that was never written. This commit is also where two
        // concurrent sends for the same case collide (same revision → ConcurrencyConflictException)
        // before either has published anything.
        await unitOfWork.SaveChangesAsync(ct);

        var context = new KnowledgeRequestContext(Guid.NewGuid(), @case.Id, turn.Id);
        var timings = new StageTimingsAccumulator();

        // architecture.md §18: trace_id/case_id/turn_id enrich every log line for the rest of this
        // turn, so per-stage and per-decision entries below never have to repeat them by hand — and
        // so a log search by trace_id (echoed to the client as X-Trace-Id) finds the whole turn.
        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["trace_id"] = context.TraceId,
            ["case_id"] = @case.Id.ToString(),
            ["turn_id"] = turn.Id.ToString(),
            ["revision"] = turn.Revision,
        });

        await PublishAsync(@case.Id, turn.Id, turn.Revision, "USER_MESSAGE", now,
            new Dictionary<string, object?> { ["text"] = messageText }, ct);

        var outcome = (await RunTurnAsync()) with { TraceId = context.TraceId };
        LogOutcome(outcome, timings);
        return outcome;

        async Task<TurnOutcome> RunTurnAsync()
        {
        try
        {
            if (await TryGetConfirmedViolationAsync(messageText, context, ct) is { } violation)
            {
                return await HandleModerationViolationAsync(@case, turn, messageText, violation, timings, now, ct);
            }

            if (DirectHumanRequestDetector.IsExplicitRequest(messageText))
            {
                return await OfferHandoffAsync(@case, turn, messageText, timings, now,
                    evidenceInsufficient: true, conditionDependent: false, [], explicitHumanRequest: true, ct,
                    reason: "EXPLICIT_HUMAN_REQUEST");
            }

            // Obvious small talk and clearly foreign topics do not need a retrieval/model call and
            // must never become an accidental Portal handoff. The scope detector is intentionally
            // conservative; anything it does not recognise continues through normal Knowledge.
            if (SupportScopeDetector.TryGetReply(messageText, out var scopeReply))
            {
                return await HandleOutOfScopeAsync(@case, turn, messageText, scopeReply, timings, now, ct);
            }

            // "Не спрашивать повторно slot после «не знаю»" (product-spec.md §11): a decline of the
            // one outstanding clarification cannot be resolved by asking again, so it goes straight
            // to handoff instead of re-running retrieval on what is effectively the same gap.
            if (@case.TurnContext.HasPendingClarification && ClarificationDeclineDetector.IsDecline(messageText))
            {
                @case.TryDeclineCurrentClarification(out _);
                return await OfferHandoffAsync(@case, turn, messageText, timings, now,
                    evidenceInsufficient: true, conditionDependent: false, [], explicitHumanRequest: false, ct,
                    reason: "CLARIFICATION_DECLINED", extraReasonCode: "CLARIFICATION_DECLINED");
            }

            var priorTurnSummary = BuildPriorTurnSummary(@case.TurnContext);
            var understand = await StageTimingsAccumulator.TimeAsync(
                () => knowledge.UnderstandAsync(new UnderstandRequest(messageText, priorTurnSummary), context, ct),
                ms => timings.UnderstandMs = ms);
            await PublishStageAsync(@case.Id, turn, "Проверяем запрос", now, ct);

            // Merge this turn's understood slots into case-wide continuity *before* building the
            // retrieve request, so retrieval sees everything known so far — not just what this one
            // message happened to restate (product-spec.md §7: "не спрашивать уже известное").
            @case.ObserveUnderstanding(understand.NormalizedText, understand.Entities.Select(ToContextSlot));
            var knownEntities = @case.TurnContext.KnownSlots.Select(ToEntity).ToArray();

            var retrieve = await StageTimingsAccumulator.TimeAsync(
                () => knowledge.RetrieveAsync(
                    new RetrieveRequest(understand.NormalizedText, knownEntities, understand.ExactCodes, Corpus.Normative, SnapshotId: null),
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
                new Dictionary<string, object?> { ["category"] = ex.Category.ToWire() }, ct);
            EnqueueQualityTurnPush(@case, turn, messageText, Decision.TechnicalError, timings,
                answerMarkdown: null, evidenceFragmentIds: null, snapshotId: null, routing: null, errorCategory: ex.Category.ToWire());
            return TurnOutcome.TechnicalError(turn.Id, turn.Revision, @case.ModerationWarningCount, ex.Category);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Not a classified knowledge failure — a defect or an infrastructure error the caller
            // must still see (it propagates). But the QUEUED row from the first commit would
            // otherwise sit there until `stale-turn cleanup`'s TTL; close it now so the user gets
            // TECHNICAL_ERROR immediately. Best effort: if this second commit fails too, the
            // cleanup worker remains the fallback.
            logger.LogError(ex, "Unclassified failure in turn {TurnId} revision {Revision}", turn.Id, turn.Revision);
            await TryFailTurnBestEffortAsync(@case, turn, now);
            throw;
        }
        }
    }

    /// <summary>architecture.md §18: one structured line per turn — decision, reason codes, error category, and stage latencies, all keyed by the trace/case/turn scope <see cref="RunAsync"/> opened.</summary>
    private void LogOutcome(TurnOutcome outcome, StageTimingsAccumulator timings)
    {
        if (outcome.FailureCategory is { } category)
        {
            logger.LogWarning(
                "Turn failed: decision={Decision} error_category={ErrorCategory} understand_ms={UnderstandMs} retrieve_ms={RetrieveMs} draft_ms={DraftMs} verify_ms={VerifyMs}",
                outcome.Decision, category, timings.UnderstandMs, timings.RetrieveMs, timings.DraftMs, timings.VerifyMs);
            return;
        }

        logger.LogInformation(
            "Turn decided: decision={Decision} understand_ms={UnderstandMs} retrieve_ms={RetrieveMs} draft_ms={DraftMs} verify_ms={VerifyMs}",
            outcome.Decision, timings.UnderstandMs, timings.RetrieveMs, timings.DraftMs, timings.VerifyMs);
    }

    private async Task TryFailTurnBestEffortAsync(Case @case, Turn turn, DateTimeOffset now)
    {
        if (!@case.TryFailTurn(turn.Id, turn.Revision))
        {
            return;
        }

        try
        {
            await PublishAsync(@case.Id, turn.Id, turn.Revision, "TECHNICAL_ERROR", now,
                new Dictionary<string, object?> { ["category"] = InternalErrorCategory }, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Swallowed on purpose: the original exception is the one worth surfacing, and the
            // persisted QUEUED turn is still recoverable by `stale-turn cleanup`.
        }
    }

    /// <summary>
    /// A confirmed-list rule hit is a confirmed violation outright. An ambiguous hit is escalated to
    /// `knowledge.AssessModerationContextAsync`; only `OFFENSIVE` confirms it — `UNCERTAIN`/`NOT_OFFENSIVE`
    /// let the turn continue normally (product-spec.md §14 step 4).
    /// </summary>
    private async Task<ModerationRuleMatch?> TryGetConfirmedViolationAsync(string messageText, KnowledgeRequestContext context, CancellationToken ct)
    {
        var match = moderationRules.Evaluate(messageText);
        if (match is null)
        {
            return null;
        }

        if (!match.RequiresContextCheck)
        {
            return match;
        }

        var assessment = await knowledge.AssessModerationContextAsync(
            new ModerationContextRequest(messageText, match.RuleId, match.RuleVersion, match.MatchedTerm, match.Start, match.End), context, ct);
        return assessment.Ambiguity == ModerationAmbiguity.Offensive ? match : null;
    }

    private async Task<TurnOutcome> HandleModerationViolationAsync(
        Case @case, Turn turn, string messageText, ModerationRuleMatch violation, StageTimingsAccumulator timings, DateTimeOffset now, CancellationToken ct)
    {
        var decision = @case.RecordModerationViolation(moderationOptions.CloseAfterWarnings, now);

        // The user-facing text and every field a client could otherwise derive itself come from the
        // server event, never from local UI state (product-spec.md §14, web-api-v0.md §7).
        var message = decision == Decision.ModerationClose
            ? "Обращение закрыто из-за повторного нарушения правил общения. Вы можете открыть новое обращение."
            : $"Предупреждение {@case.ModerationWarningCount} из {moderationOptions.CloseAfterWarnings}. " +
              "При повторном нарушении чат будет закрыт.";

        await PublishAsync(@case.Id, turn.Id, turn.Revision, decision == Decision.ModerationClose ? "CONVERSATION_CLOSED" : "MODERATION_WARNING",
            now, new Dictionary<string, object?>
            {
                ["moderation_warning_count"] = @case.ModerationWarningCount,
                ["close_after_warnings"] = moderationOptions.CloseAfterWarnings,
                ["rule_id"] = violation.RuleId,
                ["rule_version"] = violation.RuleVersion,
                ["message"] = message,
            }, ct);
        EnqueueQualityTurnPush(@case, turn, messageText, decision, timings,
            answerMarkdown: null, evidenceFragmentIds: null, snapshotId: null, routing: null, errorCategory: null);

        if (decision == Decision.ModerationClose)
        {
            // architecture.md §5.8: completion emits CASE_COMPLETED + a notification for every close
            // path, moderation included — it only skips FEEDBACK_REQUESTED, which
            // CaseCompletionPublisher already decides on its own from CompletionReason.Moderation
            // (set by Case.RecordModerationViolation above). Reusing it here is the only way this
            // path was missing CASE_COMPLETED/the notification at all (B5).
            await completionPublisher.PublishAsync(@case, @case.ResolutionStatus, now, ct);
        }

        return TurnOutcome.Moderation(turn.Id, turn.Revision, decision, @case.ModerationWarningCount);
    }

    private async Task<TurnOutcome> HandleOutOfScopeAsync(
        Case @case,
        Turn turn,
        string messageText,
        string reply,
        StageTimingsAccumulator timings,
        DateTimeOffset now,
        CancellationToken ct)
    {
        @case.ResetClarificationLoop();
        @case.TryPublishDecision(turn.Id, turn.Revision, Decision.OutOfScope);
        await PublishAsync(@case.Id, turn.Id, turn.Revision, "OUT_OF_SCOPE", now,
            new Dictionary<string, object?>
            {
                ["message"] = reply,
                ["reason"] = "OUT_OF_SCOPE",
            }, ct);
        EnqueueQualityTurnPush(@case, turn, messageText, Decision.OutOfScope, timings,
            answerMarkdown: null, evidenceFragmentIds: null, snapshotId: null, routing: null, errorCategory: null);
        return TurnOutcome.OutOfScope(turn.Id, turn.Revision, @case.ModerationWarningCount);
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
        CancellationToken ct,
        bool expandedOnce = false)
    {
        if (answerability.RiskFlags.Contains(KnowledgeUnavailableRiskFlag))
        {
            throw new KnowledgeFailureException(KnowledgeFailureCategory.Unavailable, "knowledge reported its repository as unavailable.");
        }

        switch (answerability.EvidenceSufficiency)
        {
            case EvidenceSufficiency.ConditionDependent:
                if (@case.ClarificationLimitReached)
                {
                    // "не более двух последовательных clarifications" (product-spec.md §11) — a
                    // third would-be question in the same unresolved scenario escalates instead.
                    return await OfferHandoffAsync(@case, turn, messageText, timings, now,
                        evidenceInsufficient: false, conditionDependent: true, answerability.RiskFlags, explicitHumanRequest: false, ct, retrieve.SnapshotId,
                        reason: "CLARIFICATION_LIMIT", extraReasonCode: "CLARIFICATION_LIMIT",
                        checkedFragmentIds: retrieve.Candidates.Select(c => c.FragmentId).ToArray());
                }

                @case.RecordClarification(answerability.MissingConditions);
                @case.TryPublishDecision(turn.Id, turn.Revision, Decision.Clarify);
                await PublishAsync(@case.Id, turn.Id, turn.Revision, "CLARIFICATION", now,
                    new Dictionary<string, object?>
                    {
                        ["missing_conditions"] = answerability.MissingConditions,
                        ["questions"] = answerability.MissingConditions.Select(ToClarificationQuestion).ToArray(),
                    }, ct);
                EnqueueQualityTurnPush(@case, turn, messageText, Decision.Clarify, timings,
                    answerMarkdown: null, evidenceFragmentIds: null, retrieve.SnapshotId, routing: null, errorCategory: null);
                return TurnOutcome.Clarify(turn.Id, turn.Revision, @case.ModerationWarningCount, answerability.MissingConditions);

            case EvidenceSufficiency.Insufficient:
                // product-spec.md §8 п.9: "максимум одно дополнительное расширение поиска" — the
                // first retrieve was narrowed by an exact code/status that did not match anything
                // applicable; one broader retry without it before giving up, rather than failing
                // solely because that one exact code produced nothing. Guarded by `expandedOnce` so
                // this can only ever fire once per turn, however the recursive call below lands.
                if (!expandedOnce && understand.ExactCodes.Count > 0)
                {
                    var knownEntitiesForExpansion = @case.TurnContext.KnownSlots.Select(ToEntity).ToArray();
                    var expandedRetrieve = await StageTimingsAccumulator.TimeAsync(
                        () => knowledge.RetrieveAsync(
                            new RetrieveRequest(understand.NormalizedText, knownEntitiesForExpansion, ExactCodes: [], Corpus.Normative, retrieve.SnapshotId),
                            context, ct),
                        ms => timings.RetrieveMs = (timings.RetrieveMs ?? 0) + ms);
                    var expandedCandidateIds = expandedRetrieve.Candidates.Select(c => c.FragmentId).ToArray();
                    var expandedAnswerability = await knowledge.AssessAnswerabilityAsync(
                        new AnswerabilityRequest(understand.NormalizedText, expandedRetrieve.SnapshotId, expandedCandidateIds), context, ct);

                    if (expandedAnswerability.EvidenceSufficiency != EvidenceSufficiency.Insufficient)
                    {
                        // The broader search actually turned up something applicable.
                        return await DecideFromAnswerabilityAsync(
                            @case, turn, messageText, understand, expandedRetrieve, expandedAnswerability, context, timings, now, ct, expandedOnce: true);
                    }

                    return await OfferHandoffAsync(@case, turn, messageText, timings, now,
                        evidenceInsufficient: true, conditionDependent: false, expandedAnswerability.RiskFlags, explicitHumanRequest: false, ct, expandedRetrieve.SnapshotId,
                        reason: "INSUFFICIENT_EVIDENCE", extraReasonCode: "EXPANDED_RETRY", checkedFragmentIds: expandedCandidateIds);
                }

                return await OfferHandoffAsync(@case, turn, messageText, timings, now,
                    evidenceInsufficient: true, conditionDependent: false, answerability.RiskFlags, explicitHumanRequest: false, ct, retrieve.SnapshotId,
                    reason: "INSUFFICIENT_EVIDENCE", checkedFragmentIds: retrieve.Candidates.Select(c => c.FragmentId).ToArray());

            case EvidenceSufficiency.Sufficient:
                var draft = await StageTimingsAccumulator.TimeAsync(
                    () => knowledge.DraftAsync(
                        new DraftRequest(understand.NormalizedText, retrieve.SnapshotId, answerability.EvidenceFragmentIds),
                        context, ct),
                    ms => timings.DraftMs = ms);
                var verify = await StageTimingsAccumulator.TimeAsync(
                    () => knowledge.VerifyAsync(new VerifyRequest(draft.Claims, retrieve.SnapshotId), context, ct),
                    ms => timings.VerifyMs = ms);
                var verified = verify.Results.Count > 0 && verify.Results.All(r => r.Supported);

                if (!verified)
                {
                    // product-spec.md §12: "один ограниченный rewrite/экстрактивный fallback" before
                    // giving up — ask once more for a more conservative, closer-to-source draft
                    // rather than handing off after a single failed claim.
                    draft = await StageTimingsAccumulator.TimeAsync(
                        () => knowledge.DraftAsync(
                            new DraftRequest(understand.NormalizedText, retrieve.SnapshotId, answerability.EvidenceFragmentIds, Tone: "extractive"),
                            context, ct),
                        ms => timings.DraftMs = (timings.DraftMs ?? 0) + ms);
                    verify = await StageTimingsAccumulator.TimeAsync(
                        () => knowledge.VerifyAsync(new VerifyRequest(draft.Claims, retrieve.SnapshotId), context, ct),
                        ms => timings.VerifyMs = (timings.VerifyMs ?? 0) + ms);
                    verified = verify.Results.Count > 0 && verify.Results.All(r => r.Supported);
                }

                await PublishStageAsync(@case.Id, turn, "Готовим ответ / передачу", now, ct);

                if (!verified)
                {
                    // Still unsupported after the extractive rewrite: do not publish an unsupported
                    // answer — offer human support instead. Reason kept distinct from
                    // INSUFFICIENT_EVIDENCE so Knowledge Gap Radar (product-experience.md §8) never
                    // conflates "no applicable knowledge" with "a draft existed but failed verification".
                    return await OfferHandoffAsync(@case, turn, messageText, timings, now,
                        evidenceInsufficient: true, conditionDependent: false, answerability.RiskFlags, explicitHumanRequest: false, ct, retrieve.SnapshotId,
                        reason: "VERIFICATION_FAILED", checkedFragmentIds: answerability.EvidenceFragmentIds);
                }

                // A confirmed answer resolves whatever clarification scenario was open — the next
                // unrelated question starts its own fresh loop (product-spec.md §11).
                @case.ResetClarificationLoop();

                // web-api-v0.md §4.3: sources carry document_id/page/anchor/title, not just the bare
                // fragment id — resolved from this turn's own retrieve candidates so the browser
                // never needs a second round trip just to label the source button.
                var sources = BuildAnswerSources(retrieve.Candidates, answerability.EvidenceFragmentIds);

                if (answerability.RiskFlags.Count > 0)
                {
                    // A confirmed, verified answer, but the situation/instruction itself still needs
                    // support to act on (product-spec.md §10 ANSWER_AND_HANDOFF).
                    var routing = RoutingPolicy.Evaluate(evidenceInsufficient: false, conditionDependent: false, answerability.RiskFlags);
                    @case.TryPublishDecision(turn.Id, turn.Revision, Decision.AnswerAndHandoff);
                    await PublishAnswerAsync(@case, turn, now, draft.Markdown, sources, retrieve.SnapshotId, draft.ModelVersion, retrieve.RetrievalConfigVersion, answerability, ct);
                    await PublishHandoffOfferAsync(@case, turn, now, routing, ct);
                    EnqueueQualityTurnPush(@case, turn, messageText, Decision.AnswerAndHandoff, timings,
                        draft.Markdown, answerability.EvidenceFragmentIds, retrieve.SnapshotId, routing, errorCategory: null,
                        draft.ModelVersion, retrieve.RetrievalConfigVersion);
                    return TurnOutcome.AnsweredWithHandoff(turn.Id, turn.Revision, @case.ModerationWarningCount, draft.Markdown, sources);
                }

                @case.TryPublishDecision(turn.Id, turn.Revision, Decision.Answer);
                await PublishAnswerAsync(@case, turn, now, draft.Markdown, sources, retrieve.SnapshotId, draft.ModelVersion, retrieve.RetrievalConfigVersion, answerability, ct);
                EnqueueQualityTurnPush(@case, turn, messageText, Decision.Answer, timings,
                    draft.Markdown, answerability.EvidenceFragmentIds, retrieve.SnapshotId, routing: null, errorCategory: null,
                    draft.ModelVersion, retrieve.RetrievalConfigVersion);
                return TurnOutcome.Answered(turn.Id, turn.Revision, @case.ModerationWarningCount, draft.Markdown, sources);

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
        string? snapshotId = null,
        string reason = "INSUFFICIENT_EVIDENCE",
        string? extraReasonCode = null,
        IReadOnlyList<string>? checkedFragmentIds = null)
    {
        // Every handoff-offer path ends whatever clarification scenario was open — a human takes it
        // from here, so a later unrelated question starts its own fresh loop (product-spec.md §11).
        @case.ResetClarificationLoop();

        @case.TryPublishDecision(turn.Id, turn.Revision, Decision.HandoffOffer);
        // `reason` is a single canonical top-level cause (product-experience.md §8 Knowledge Gap
        // Radar needs to tell "no applicable knowledge" apart from "a draft existed but failed
        // verification" apart from "user declined the one open clarification", etc.) — distinct from
        // `routing.reason_codes` below, which can carry several risk-flag-derived codes at once.
        // `checked_fragment_ids` is what `HandoffPackageBuilder` reads back as `sources_checked`
        // (support-adapter-v0.md §2) when no answer was ever confirmed to carry its own `sources`.
        await PublishAsync(@case.Id, turn.Id, turn.Revision, "NO_CONFIRMED_ANSWER", now,
            new Dictionary<string, object?> { ["reason"] = reason, ["checked_fragment_ids"] = checkedFragmentIds ?? [] }, ct);

        var routing = RoutingPolicy.Evaluate(evidenceInsufficient, conditionDependent, riskFlags, explicitHumanRequest);
        if (extraReasonCode is not null)
        {
            routing = routing with { ReasonCodes = [.. routing.ReasonCodes, extraReasonCode] };
        }

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
        string? errorCategory,
        string? modelVersion = null,
        string? retrievalConfigVersion = null) =>
        outbox.Enqueue(QualityOutboxMessages.Turn, new QualityTurnPush(
            @case.Id.ToString(),
            turn.Id.ToString(),
            turn.Revision,
            turn.CreatedAt,
            messageText,
            PriorTurnsSummary: null,
            decision.ToWire(),
            routing?.ReasonCodes ?? [],
            answerMarkdown,
            evidenceFragmentIds,
            snapshotId,
            HandoffStatus: @case.Handoff?.Status.ToWire(),
            RecommendedLine: routing?.RecommendedLine.ToWire(),
            ServiceNeed: routing?.ServiceNeed.ToWire(),
            timings.ToPush(),
            errorCategory,
            modelVersion,
            retrievalConfigVersion));

    /// <summary>
    /// The source ids ride on the event too, so a reloaded timeline can still open «источник» — the
    /// POST response is not the only carrier. `snapshot_id`/`model_version`/`retrieval_config_version`
    /// are recorded here (architecture.md §8 "Knowledge versioning": "every user-facing answer
    /// records at least snapshot ID, fragment IDs, model/retrieval config version") — without this
    /// they existed only in local variables for the duration of the request and were unrecoverable
    /// afterwards, making demo/debug reproduction of a specific answer impossible.
    /// </summary>
    private Task PublishAnswerAsync(
        Case @case, Turn turn, DateTimeOffset now, string markdown, IReadOnlyList<AnswerSource> sources,
        string snapshotId, string modelVersion, string retrievalConfigVersion, AnswerabilityResult answerability, CancellationToken ct) =>
        PublishAsync(@case.Id, turn.Id, turn.Revision, "AI_ANSWER", now, new Dictionary<string, object?>
        {
            ["markdown"] = markdown,
            // Same shape as the answer/source payload (web-api-v0.md §4.2 "the same shape" / §4.3):
            // fragment_id/title/page/label only — document_id/anchor are an internal retrieval
            // detail resolved lazily via GET /sources/{fragment_id} when the user opens the source.
            ["sources"] = sources.Select(s => new { fragment_id = s.FragmentId, title = s.Title, page = s.Page, label = "Открыть источник" }).ToArray(),
            ["snapshot_id"] = snapshotId,
            ["model_version"] = modelVersion,
            ["retrieval_config_version"] = retrievalConfigVersion,
            // product-experience.md §5 Applicability Card: the same applicability facts that passed
            // the Answerability Gate, not a re-derived or invented confidence score. `entities` come
            // from case-wide known slots (product-spec.md §7 provenance) so the card can render
            // "✓ Роль: поставщик (user_explicit)" without a second round trip.
            ["applicability"] = new
            {
                entities = @case.TurnContext.KnownSlots.Select(s => new { type = s.Type, value = s.Value, provenance = ToWireProvenance(s.Provenance) }).ToArray(),
                missing_conditions = answerability.MissingConditions,
                // F1.3 (docs/plans/active/2026-09-product-enhancements.md): reuse B1's slot→question
                // mapping so the Applicability Card's "Нужно уточнить" block never has to render a
                // raw slot id either.
                questions = answerability.MissingConditions.Select(ToClarificationQuestion).ToArray(),
                risk_flags = answerability.RiskFlags,
                evidence_fragment_ids = answerability.EvidenceFragmentIds,
            },
        }, ct);

    /// <summary>
    /// B1 (docs/plans/active/2026-09-demo-readiness.md): `missing_conditions` slot ids come straight
    /// through from `knowledge` (e.g. `"role"`, `"provider"`) and are not user-facing copy on their
    /// own. `questions` is an additive companion field (web-api-v0.md §4.2) carrying an actual
    /// question the UI can render; a slot without a known mapping still gets a usable, if generic,
    /// question rather than being dropped.
    /// </summary>
    private static string ToClarificationQuestion(string slot) => slot switch
    {
        "role" => "Вы работаете на Портале как поставщик или как заказчик?",
        "provider" => "Через какого оператора ЭДО вы работаете?",
        "process" => "Какой процесс или раздел Портала вы сейчас проходите?",
        "document_status" => "Какой статус сейчас отображается у документа?",
        "status" => "Какой сейчас статус документа?",
        "document_type" => "Какой тип документа вы оформляете?",
        "attempted_action" => "Какое действие вы выполняли перед проблемой?",
        "duration" => "Как долго статус или ошибка остаётся без изменений?",
        "already_tried" => "Что именно вы уже попробовали сделать?",
        "error_code" => "Какой код ошибки или полный текст сообщения вы видите?",
        "category_id" => "Какой идентификатор категории указан в файле?",
        "model" => "Какую модель или формат вы используете?",
        "source_state" => "Какое состояние документа указано в Портале?",
        _ => "Уточните детали ситуации, чтобы подобрать точную инструкцию.",
    };

    private static string ToWireProvenance(ContextSlotProvenance provenance) => provenance switch
    {
        ContextSlotProvenance.UserExplicit => "user_explicit",
        ContextSlotProvenance.TrustedPortalContext => "trusted_portal_context",
        ContextSlotProvenance.Inferred => "inferred",
        _ => "unknown",
    };

    /// <summary>
    /// Resolves each evidence fragment id to the retrieve candidate that carries its
    /// document/page/anchor/title (web-api-v0.md §4.3). A fragment id `answerability` referenced but
    /// `retrieve` returned no metadata for (defensive — should not happen on a real pipeline) still
    /// gets a source entry, just with the id standing in for both document id and title, rather than
    /// silently dropping real evidence.
    /// </summary>
    private static IReadOnlyList<AnswerSource> BuildAnswerSources(IReadOnlyList<RetrievalCandidate> candidates, IReadOnlyList<string> evidenceFragmentIds)
    {
        var byFragmentId = candidates.GroupBy(c => c.FragmentId).ToDictionary(g => g.Key, g => g.First());

        return
        [
            .. evidenceFragmentIds.Select(fragmentId => byFragmentId.TryGetValue(fragmentId, out var candidate)
                ? new AnswerSource(candidate.FragmentId, candidate.DocumentId, candidate.Page, candidate.Anchor, candidate.Title ?? candidate.DocumentId)
                : new AnswerSource(fragmentId, fragmentId, null, null, fragmentId)),
        ];
    }

    private Task PublishHandoffOfferAsync(Case @case, Turn turn, DateTimeOffset now, RoutingDecision routing, CancellationToken ct) =>
        PublishAsync(@case.Id, turn.Id, turn.Revision, "HANDOFF_OFFER", now, new Dictionary<string, object?>
        {
            ["service_need"] = routing.ServiceNeed.ToWire(),
            ["recommended_line"] = routing.RecommendedLine.ToWire(),
            ["dispatch_queue"] = routing.DispatchQueue,
            ["engineering_review_suggested"] = routing.EngineeringReviewSuggested,
            ["reason_codes"] = routing.ReasonCodes,
        }, ct);

    /// <summary>
    /// Compact continuity text for `understand`'s `prior_turn_summary` (product-spec.md §7): the
    /// previous unresolved question, everything already known with its slot values, and what was
    /// still missing last time — so `knowledge` is not asked to re-derive the whole scenario from a
    /// single follow-up message like "поставщик".
    /// </summary>
    private static string? BuildPriorTurnSummary(TurnContext turnContext)
    {
        if (turnContext.LastQuestionText is null && turnContext.KnownSlots.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (turnContext.LastQuestionText is { } question)
        {
            parts.Add($"Предыдущий вопрос: {question}");
        }

        if (turnContext.KnownSlots.Count > 0)
        {
            parts.Add("Известно: " + string.Join("; ", turnContext.KnownSlots.Select(s => $"{s.Type}={s.Value}")));
        }

        if (turnContext.LastMissingConditions.Count > 0)
        {
            parts.Add("Уточняли: " + string.Join("; ", turnContext.LastMissingConditions));
        }

        return string.Join("\n", parts);
    }

    private static ContextSlot ToContextSlot(Entity entity) => new(
        entity.Type,
        entity.Value,
        entity.Provenance switch
        {
            EntityProvenance.UserExplicit => ContextSlotProvenance.UserExplicit,
            EntityProvenance.TrustedPortalContext => ContextSlotProvenance.TrustedPortalContext,
            EntityProvenance.Inferred => ContextSlotProvenance.Inferred,
            _ => ContextSlotProvenance.Unknown,
        });

    private static Entity ToEntity(ContextSlot slot) => new(
        slot.Type,
        slot.Value,
        slot.Provenance switch
        {
            ContextSlotProvenance.UserExplicit => EntityProvenance.UserExplicit,
            ContextSlotProvenance.TrustedPortalContext => EntityProvenance.TrustedPortalContext,
            ContextSlotProvenance.Inferred => EntityProvenance.Inferred,
            _ => EntityProvenance.Unknown,
        });

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
