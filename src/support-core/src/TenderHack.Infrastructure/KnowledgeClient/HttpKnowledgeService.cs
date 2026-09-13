using Microsoft.Extensions.Options;
using TenderHack.Domain.Feedback;
using AppKnowledge = TenderHack.Application.Knowledge;
using Generated = TenderHack.Infrastructure.KnowledgeClient.Generated;

namespace TenderHack.Infrastructure.KnowledgeClient;

/// <summary>
/// Implements the `Application` port over the NSwag-generated client (knowledge-v0.md). Owns the
/// only translation between generated wire DTOs and Application's own records, and the only place
/// that classifies a failed call into <see cref="AppKnowledge.KnowledgeFailureException"/>
/// (knowledge-v0.md §3) — nothing generated escapes past this file. Also owns per-stage timeout
/// enforcement (architecture.md §10): each call gets its own <see cref="CancellationTokenSource"/>
/// linked to the caller's token rather than relying on one global `HttpClient.Timeout`.
/// </summary>
public sealed class HttpKnowledgeService(Generated.IKnowledgeApiClient client, IOptions<KnowledgeServiceOptions> options) : AppKnowledge.IKnowledgeService
{
    private KnowledgeStageTimeouts Timeouts => options.Value.Timeouts;

    /// <summary>architecture.md §7: idempotent stages get up to 2 retries (3 attempts total) of a `TIMEOUT`/`UNAVAILABLE` failure.</summary>
    private const int IdempotentStageAttempts = 3;

    /// <summary>architecture.md §7: `draft` is retried at most once — nothing is persisted before the turn commits, so a single retry is still safe.</summary>
    private const int DraftAttempts = 2;

    public async Task<AppKnowledge.UnderstandResult> UnderstandAsync(
        AppKnowledge.UnderstandRequest request, AppKnowledge.KnowledgeRequestContext context, CancellationToken ct)
    {
        var response = await CallAsync(
            token => client.UnderstandAsync(
                context.TraceId,
                context.CaseId.ToString(),
                context.TurnId.ToString(),
                new Generated.UnderstandRequest { Text = request.Text, Prior_turn_summary = request.PriorTurnSummary },
                token), Timeouts.Understand, ct, IdempotentStageAttempts);

        return new AppKnowledge.UnderstandResult(
            response.Normalized_text,
            [.. response.Entities.Select(ToAppEntity)],
            [.. response.Exact_codes],
            [response.Language_flags.Detected_language, response.Language_flags.Typo_corrected ? "typo_corrected" : "typo_clean"]);
    }

    public async Task<AppKnowledge.ModerationContextResult> AssessModerationContextAsync(
        AppKnowledge.ModerationContextRequest request, AppKnowledge.KnowledgeRequestContext context, CancellationToken ct)
    {
        var ruleMatch = new Generated.RuleMatch
        {
            Rule_id = request.RuleId,
            Version = request.RuleVersion,
            Matched_term = request.MatchedTerm,
            Matched_span = new Generated.Matched_span { Start = request.Start, End = request.End },
        };

        var response = await CallAsync(
            token => client.ModerationContextAsync(
                context.TraceId,
                context.CaseId.ToString(),
                context.TurnId.ToString(),
                new Generated.ModerationContextRequest { Text = request.Text, Rule_match = ruleMatch },
                token), Timeouts.ModerationContext, ct, IdempotentStageAttempts);

        var ambiguity = response.Ambiguity switch
        {
            Generated.ModerationAmbiguity.OFFENSIVE => AppKnowledge.ModerationAmbiguity.Offensive,
            Generated.ModerationAmbiguity.NOT_OFFENSIVE => AppKnowledge.ModerationAmbiguity.NotOffensive,
            _ => AppKnowledge.ModerationAmbiguity.Uncertain,
        };

        return new AppKnowledge.ModerationContextResult(ambiguity, response.Model_version);
    }

    public async Task<AppKnowledge.RetrieveResult> RetrieveAsync(
        AppKnowledge.RetrieveRequest request, AppKnowledge.KnowledgeRequestContext context, CancellationToken ct)
    {
        var response = await CallAsync(
            token => client.RetrieveAsync(
                context.TraceId,
                context.CaseId.ToString(),
                context.TurnId.ToString(),
                new Generated.RetrieveRequest
                {
                    Query = request.Query,
                    Entities = [.. request.Entities.Select(ToGeneratedEntity)],
                    Exact_codes = [.. request.ExactCodes],
                    Corpus = request.Corpus == AppKnowledge.Corpus.Historical
                        ? Generated.Corpus.HISTORICAL
                        : Generated.Corpus.NORMATIVE,
                    Snapshot_id = request.SnapshotId,
                },
                token), Timeouts.Retrieve, ct, IdempotentStageAttempts);

        return new AppKnowledge.RetrieveResult(
            response.Snapshot_id,
            response.Retrieval_config_version,
            [.. response.Candidates.Select(c => new AppKnowledge.RetrievalCandidate(c.Fragment_id, c.Document_id, c.Page, c.Anchor, c.Title))]);
    }

    public async Task<AppKnowledge.AnswerabilityResult> AssessAnswerabilityAsync(
        AppKnowledge.AnswerabilityRequest request, AppKnowledge.KnowledgeRequestContext context, CancellationToken ct)
    {
        var response = await CallAsync(
            token => client.AnswerabilityAsync(
                context.TraceId,
                context.CaseId.ToString(),
                context.TurnId.ToString(),
                new Generated.AnswerabilityRequest
                {
                    Query = request.Query,
                    Snapshot_id = request.SnapshotId,
                    Candidate_fragment_ids = [.. request.CandidateFragmentIds],
                },
                token), Timeouts.Answerability, ct, IdempotentStageAttempts);

        var evidenceSufficiency = response.Evidence_sufficiency switch
        {
            Generated.EvidenceSufficiency.SUFFICIENT => AppKnowledge.EvidenceSufficiency.Sufficient,
            Generated.EvidenceSufficiency.CONDITION_DEPENDENT => AppKnowledge.EvidenceSufficiency.ConditionDependent,
            _ => AppKnowledge.EvidenceSufficiency.Insufficient,
        };

        return new AppKnowledge.AnswerabilityResult(
            evidenceSufficiency,
            [.. response.Evidence_fragment_ids],
            [.. response.Missing_conditions],
            [.. response.Risk_flags]);
    }

    public async Task<AppKnowledge.DraftResult> DraftAsync(
        AppKnowledge.DraftRequest request, AppKnowledge.KnowledgeRequestContext context, CancellationToken ct)
    {
        var response = await CallAsync(
            token => client.DraftAsync(
                context.TraceId,
                context.CaseId.ToString(),
                context.TurnId.ToString(),
                new Generated.DraftRequest
                {
                    Query = request.Query,
                    Snapshot_id = request.SnapshotId,
                    Evidence_fragment_ids = [.. request.EvidenceFragmentIds],
                    Constraints = new Generated.DraftConstraints { Tone = request.Tone },
                },
                token), Timeouts.Draft, ct, DraftAttempts);

        return new AppKnowledge.DraftResult(
            response.Draft_markdown,
            [.. response.Claims.Select(c => new AppKnowledge.DraftClaim(c.Claim_id, c.Text, [.. c.Fragment_ids]))],
            response.Model_version);
    }

    public async Task<AppKnowledge.VerifyResult> VerifyAsync(
        AppKnowledge.VerifyRequest request, AppKnowledge.KnowledgeRequestContext context, CancellationToken ct)
    {
        var response = await CallAsync(
            token => client.VerifyAsync(
                context.TraceId,
                context.CaseId.ToString(),
                context.TurnId.ToString(),
                new Generated.VerifyRequest
                {
                    Claims = [.. request.Claims.Select(c => new Generated.Claim { Claim_id = c.ClaimId, Text = c.Text, Fragment_ids = [.. c.FragmentIds] })],
                    Snapshot_id = request.SnapshotId,
                },
                token), Timeouts.Verify, ct, IdempotentStageAttempts);

        return new AppKnowledge.VerifyResult(
            [.. response.Results.Select(r => new AppKnowledge.ClaimVerification(r.Claim_id, r.Supported, [.. r.Evidence_fragment_ids]))]);
    }

    public async Task<AppKnowledge.SourceFragment> GetSourceAsync(string fragmentId, CancellationToken ct)
    {
        var traceId = Guid.NewGuid();
        var response = await CallAsync(token => client.GetSourceAsync(traceId, fragmentId, token), Timeouts.Source, ct);

        return new AppKnowledge.SourceFragment(
            response.Document_id, response.Title, response.Version, response.Page, response.Anchor, response.Text, response.Snapshot_id);
    }

    public async Task<AppKnowledge.Materials> ListMaterialsAsync(CancellationToken ct)
    {
        var response = await CallAsync(token => client.ListMaterialsAsync(Guid.NewGuid(), token), Timeouts.Source, ct);
        return new AppKnowledge.Materials(
            response.Snapshot_id,
            [.. response.Materials.Select(m => new AppKnowledge.MaterialSummary(m.Document_id, m.Title, m.Declared_version, m.Declared_date, m.Page_count, m.Fragment_count))]);
    }

    public async Task<AppKnowledge.MaterialSections> ListMaterialSectionsAsync(string documentId, CancellationToken ct)
    {
        var response = await CallAsync(token => client.ListMaterialSectionsAsync(Guid.NewGuid(), documentId, token), Timeouts.Source, ct);
        return new AppKnowledge.MaterialSections(
            response.Snapshot_id,
            response.Document_id,
            [.. response.Sections.Select(s => new AppKnowledge.MaterialSection(s.Section, s.Page_start, s.Page_end, s.First_fragment_id))]);
    }

    public Task PushQualityTurnAsync(AppKnowledge.QualityTurnPush push, CancellationToken ct) =>
        CallAsync(token => client.PushQualityTurnAsync(Guid.NewGuid(), new Generated.QualityTurnPush
        {
            Case_id = push.CaseId,
            Turn_id = push.TurnId,
            Revision = push.Revision,
            Occurred_at = push.OccurredAt,
            Question_text = push.QuestionText,
            Prior_turns_summary = push.PriorTurnsSummary,
            Decision = push.Decision,
            Reason_codes = [.. push.ReasonCodes],
            Answer_markdown = push.AnswerMarkdown,
            Evidence_fragment_ids = push.EvidenceFragmentIds is null ? null : [.. push.EvidenceFragmentIds],
            Snapshot_id = push.SnapshotId,
            Handoff_status = push.HandoffStatus,
            Recommended_line = push.RecommendedLine,
            Service_need = push.ServiceNeed,
            Stage_timings = new Generated.StageTimings
            {
                Understand_ms = push.StageTimings.UnderstandMs,
                Retrieve_ms = push.StageTimings.RetrieveMs,
                Draft_ms = push.StageTimings.DraftMs,
                Verify_ms = push.StageTimings.VerifyMs,
            },
            Error_category = push.ErrorCategory,
            Model_version = push.ModelVersion,
            Retrieval_config_version = push.RetrievalConfigVersion,
        }, token), Timeouts.Background, ct);

    public Task PushQualityFeedbackAsync(AppKnowledge.QualityFeedbackPush push, CancellationToken ct) =>
        CallAsync(token => client.PushQualityFeedbackAsync(Guid.NewGuid(), new Generated.QualityFeedbackPush
        {
            Feedback_id = push.FeedbackId,
            Case_id = push.CaseId,
            Turn_id = push.TurnId,
            Occurred_at = push.OccurredAt,
            Specialist_rating = ToGeneratedFeedbackRating(push.SpecialistRating),
            Information_quality_rating = ToGeneratedFeedbackRating(push.InformationQualityRating),
            Specialist_ref = push.SpecialistRef,
            Integration_mode = push.IntegrationMode switch
            {
                "REAL" => Generated.QualityFeedbackPushIntegration_mode.REAL,
                "SIMULATED" => Generated.QualityFeedbackPushIntegration_mode.SIMULATED,
                _ => null,
            },
            Solved = push.Solved,
            Comment_text = push.CommentText,
        }, token), Timeouts.Background, ct);

    public Task PushQualityCompletionAsync(AppKnowledge.QualityCompletionPush push, CancellationToken ct) =>
        CallAsync(token => client.PushQualityCompletionAsync(Guid.NewGuid(), new Generated.QualityCompletionPush
        {
            Case_id = push.CaseId,
            Completed_at = push.CompletedAt,
            Completion_reason = push.CompletionReason,
            Resolution_status = push.ResolutionStatus,
            Handoff_status = push.HandoffStatus,
            Integration_mode = push.IntegrationMode switch
            {
                "REAL" => Generated.QualityCompletionPushIntegration_mode.REAL,
                "SIMULATED" => Generated.QualityCompletionPushIntegration_mode.SIMULATED,
                _ => null,
            },
            Specialist_ref = push.SpecialistRef,
            Stage_code = push.StageCode,
            Moderation_warning_count = push.ModerationWarningCount,
            Turn_count = push.TurnCount,
        }, token), Timeouts.Background, ct);

    public async Task<AppKnowledge.QualityEvaluations> GetQualityEvaluationsAsync(string caseId, CancellationToken ct)
    {
        var response = await CallAsync(token => client.GetQualityEvaluationsAsync(Guid.NewGuid(), caseId, token), Timeouts.Background, ct);
        return new AppKnowledge.QualityEvaluations(response.Case_id, [.. response.Evaluations.Select(ToEvaluation)]);
    }

    public async Task<AppKnowledge.IssueGroups> GetIssueGroupsAsync(CancellationToken ct)
    {
        var response = await CallAsync(token => client.GetIssueGroupsAsync(Guid.NewGuid(), token), Timeouts.Background, ct);
        return new AppKnowledge.IssueGroups([.. response.Groups.Select(ToIssueGroup)]);
    }

    private static Generated.FeedbackRating? ToGeneratedFeedbackRating(FeedbackRating? rating) => rating switch
    {
        FeedbackRating.Positive => Generated.FeedbackRating.POSITIVE,
        FeedbackRating.Negative => Generated.FeedbackRating.NEGATIVE,
        _ => null,
    };

    private static AppKnowledge.Evaluation ToEvaluation(Generated.Evaluation e) => new(
        e.Turn_id,
        ToDimensionScore(e.Factual_support),
        ToDimensionScore(e.Completeness),
        ToDimensionScore(e.Clarity),
        ToDimensionScore(e.Next_step),
        e.Critical_error,
        e.Critical_error_reason,
        e.Evaluated_at);

    private static AppKnowledge.DimensionScore ToDimensionScore(Generated.DimensionScore s) => new(
        DimensionValueToString(s.Value), s.Evidence, s.Reason, s.Limitation, s.Improvement_suggestion);

    private static string DimensionValueToString(Generated.DimensionValue value) => value switch
    {
        Generated.DimensionValue._0 => "0",
        Generated.DimensionValue._1 => "1",
        Generated.DimensionValue._2 => "2",
        Generated.DimensionValue.NOT_APPLICABLE => "NOT_APPLICABLE",
        _ => "UNKNOWN",
    };

    private static AppKnowledge.IssueGroup ToIssueGroup(Generated.IssueGroup g) => new(
        g.Group_id, g.Label, g.N, [.. g.Representative_case_ids], g.Negative_signal_count, g.Unresolved_count,
        [.. g.Limitations], [.. g.Hypotheses.Select(ToHypothesis)]);

    private static AppKnowledge.Hypothesis ToHypothesis(Generated.Hypothesis h) => new(
        h.Type switch
        {
            Generated.HypothesisType.KNOWLEDGE_GAP => AppKnowledge.HypothesisType.KnowledgeGap,
            Generated.HypothesisType.PROCESS_ISSUE => AppKnowledge.HypothesisType.ProcessIssue,
            Generated.HypothesisType.PORTAL_ISSUE => AppKnowledge.HypothesisType.PortalIssue,
            _ => AppKnowledge.HypothesisType.Other,
        },
        h.Text,
        h.Confidence switch
        {
            Generated.HypothesisConfidence.LOW => AppKnowledge.HypothesisConfidence.Low,
            Generated.HypothesisConfidence.MEDIUM => AppKnowledge.HypothesisConfidence.Medium,
            Generated.HypothesisConfidence.HIGH => AppKnowledge.HypothesisConfidence.High,
            _ => null,
        });

    private static Generated.Entity ToGeneratedEntity(AppKnowledge.Entity entity) => new()
    {
        Type = entity.Type,
        Value = entity.Value,
        Provenance = entity.Provenance switch
        {
            AppKnowledge.EntityProvenance.UserExplicit => Generated.EntityProvenance.User_explicit,
            AppKnowledge.EntityProvenance.TrustedPortalContext => Generated.EntityProvenance.Trusted_portal_context,
            AppKnowledge.EntityProvenance.Inferred => Generated.EntityProvenance.Inferred,
            _ => Generated.EntityProvenance.Unknown,
        },
    };

    private static AppKnowledge.Entity ToAppEntity(Generated.Entity entity) => new(
        entity.Type,
        entity.Value,
        entity.Provenance switch
        {
            Generated.EntityProvenance.User_explicit => AppKnowledge.EntityProvenance.UserExplicit,
            Generated.EntityProvenance.Trusted_portal_context => AppKnowledge.EntityProvenance.TrustedPortalContext,
            Generated.EntityProvenance.Inferred => AppKnowledge.EntityProvenance.Inferred,
            _ => AppKnowledge.EntityProvenance.Unknown,
        });

    /// <summary>
    /// Single funnel from any failed `knowledge` call to <see cref="AppKnowledge.KnowledgeFailureException"/>
    /// (knowledge-v0.md §3) — the orchestrator must never see a raw HTTP/JSON exception. Also the
    /// single place a per-stage <paramref name="timeout"/> is enforced: a linked
    /// <see cref="CancellationTokenSource"/> is cancelled after <paramref name="timeout"/>
    /// independently of the shared `HttpClient.Timeout`, so `draft` can run far longer than
    /// `understand` without either starving the other's budget.
    ///
    /// <paramref name="maxAttempts"/> implements architecture.md §7's retry rule: idempotent stages
    /// (understand/retrieve/answerability/verify/moderation context) may retry a `TIMEOUT`/
    /// `UNAVAILABLE` failure — never a schema/model error, which will not succeed on retry —
    /// `draft` gets at most one retry (nothing is persisted before the whole turn commits, so one
    /// retry can never double-publish a draft), and stages this contract does not name for retry
    /// (source lookups, quality pushes, analytics reads) default to a single attempt.
    /// </summary>
    private static async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, TimeSpan timeout, CancellationToken ct, int maxAttempts = 1)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AttemptOnceAsync(call, timeout, ct);
            }
            catch (AppKnowledge.KnowledgeFailureException ex) when (attempt < maxAttempts && IsRetryable(ex.Category))
            {
                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    private static bool IsRetryable(AppKnowledge.KnowledgeFailureCategory category) =>
        category is AppKnowledge.KnowledgeFailureCategory.Timeout or AppKnowledge.KnowledgeFailureCategory.Unavailable;

    private static async Task<T> AttemptOnceAsync<T>(Func<CancellationToken, Task<T>> call, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            return await call(cts.Token);
        }
        catch (Generated.KnowledgeApiClientException<Generated.ErrorResponse> ex)
        {
            throw new AppKnowledge.KnowledgeFailureException(CategoryFor(ex.Result.Code), ex.Message, ex);
        }
        catch (Generated.KnowledgeApiClientException ex)
        {
            // No typed ErrorResponse: either the status code was undeclared or the 2xx body failed
            // to deserialize. Neither is a legitimate "no information in the base" outcome.
            var category = ex.StatusCode >= 500
                ? AppKnowledge.KnowledgeFailureCategory.Unavailable
                : AppKnowledge.KnowledgeFailureCategory.InvalidResponse;
            throw new AppKnowledge.KnowledgeFailureException(category, ex.Message, ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Our caller did not cancel, so either this stage's own `cts.CancelAfter(timeout)` fired,
            // or (less likely, if misconfigured) HttpClient's own `Timeout` did. The decision is made
            // against the caller's original token, never against `ex.CancellationToken` — that one
            // is always already-cancelled by the time it reaches here.
            throw new AppKnowledge.KnowledgeFailureException(AppKnowledge.KnowledgeFailureCategory.Timeout, "knowledge call timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AppKnowledge.KnowledgeFailureException(AppKnowledge.KnowledgeFailureCategory.Unavailable, "knowledge is unreachable.", ex);
        }
    }

    private static AppKnowledge.KnowledgeFailureCategory CategoryFor(Generated.ErrorResponseCode code) => code switch
    {
        Generated.ErrorResponseCode.MODEL_UNAVAILABLE => AppKnowledge.KnowledgeFailureCategory.Unavailable,
        Generated.ErrorResponseCode.INTERNAL_ERROR => AppKnowledge.KnowledgeFailureCategory.Unavailable,
        Generated.ErrorResponseCode.MODEL_ERROR => AppKnowledge.KnowledgeFailureCategory.ModelError,
        _ => AppKnowledge.KnowledgeFailureCategory.InvalidResponse,
    };
}
