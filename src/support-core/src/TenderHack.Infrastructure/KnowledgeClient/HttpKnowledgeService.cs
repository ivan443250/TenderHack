using AppKnowledge = TenderHack.Application.Knowledge;
using Generated = TenderHack.Infrastructure.KnowledgeClient.Generated;

namespace TenderHack.Infrastructure.KnowledgeClient;

/// <summary>
/// Implements the `Application` port over the NSwag-generated client (knowledge-v0.md). Owns the
/// only translation between generated wire DTOs and Application's own records, and the only place
/// that classifies a failed call into <see cref="AppKnowledge.KnowledgeFailureException"/>
/// (knowledge-v0.md §3) — nothing generated escapes past this file.
/// </summary>
public sealed class HttpKnowledgeService(Generated.IKnowledgeApiClient client) : AppKnowledge.IKnowledgeService
{
    public async Task<AppKnowledge.UnderstandResult> UnderstandAsync(
        AppKnowledge.UnderstandRequest request, AppKnowledge.KnowledgeRequestContext context, CancellationToken ct)
    {
        var response = await CallAsync(
            () => client.UnderstandAsync(
                context.TraceId,
                context.CaseId.ToString(),
                context.TurnId.ToString(),
                new Generated.UnderstandRequest { Text = request.Text, Prior_turn_summary = request.PriorTurnSummary },
                ct));

        return new AppKnowledge.UnderstandResult(
            response.Normalized_text,
            [.. response.Entities.Select(e => e.Value)],
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
            Matched_span = new Generated.Matched_span { Start = 0, End = request.MatchedTerm.Length },
        };

        var response = await CallAsync(
            () => client.ModerationContextAsync(
                context.TraceId,
                context.CaseId.ToString(),
                context.TurnId.ToString(),
                new Generated.ModerationContextRequest { Text = request.Text, Rule_match = ruleMatch },
                ct));

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
            () => client.RetrieveAsync(
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
                ct));

        return new AppKnowledge.RetrieveResult(
            response.Snapshot_id,
            response.Retrieval_config_version,
            [.. response.Candidates.Select(c => new AppKnowledge.RetrievalCandidate(c.Fragment_id, c.Document_id, c.Page, c.Anchor))]);
    }

    public async Task<AppKnowledge.AnswerabilityResult> AssessAnswerabilityAsync(
        AppKnowledge.AnswerabilityRequest request, AppKnowledge.KnowledgeRequestContext context, CancellationToken ct)
    {
        var response = await CallAsync(
            () => client.AnswerabilityAsync(
                context.TraceId,
                context.CaseId.ToString(),
                context.TurnId.ToString(),
                new Generated.AnswerabilityRequest
                {
                    Query = request.Query,
                    Snapshot_id = request.SnapshotId,
                    Candidate_fragment_ids = [.. request.CandidateFragmentIds],
                },
                ct));

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
            () => client.DraftAsync(
                context.TraceId,
                context.CaseId.ToString(),
                context.TurnId.ToString(),
                new Generated.DraftRequest
                {
                    Query = request.Query,
                    Snapshot_id = request.SnapshotId,
                    Evidence_fragment_ids = [.. request.EvidenceFragmentIds],
                    Constraints = new Generated.DraftConstraints(),
                },
                ct));

        return new AppKnowledge.DraftResult(
            response.Draft_markdown,
            [.. response.Claims.Select(c => new AppKnowledge.DraftClaim(c.Claim_id, c.Text, [.. c.Fragment_ids]))],
            response.Model_version);
    }

    public async Task<AppKnowledge.VerifyResult> VerifyAsync(
        AppKnowledge.VerifyRequest request, AppKnowledge.KnowledgeRequestContext context, CancellationToken ct)
    {
        var response = await CallAsync(
            () => client.VerifyAsync(
                context.TraceId,
                context.CaseId.ToString(),
                context.TurnId.ToString(),
                new Generated.VerifyRequest
                {
                    Claims = [.. request.Claims.Select(c => new Generated.Claim { Claim_id = c.ClaimId, Text = c.Text, Fragment_ids = [.. c.FragmentIds] })],
                    Snapshot_id = request.SnapshotId,
                },
                ct));

        return new AppKnowledge.VerifyResult(
            [.. response.Results.Select(r => new AppKnowledge.ClaimVerification(r.Claim_id, r.Supported, [.. r.Evidence_fragment_ids]))]);
    }

    public async Task<AppKnowledge.SourceFragment> GetSourceAsync(string fragmentId, CancellationToken ct)
    {
        var traceId = Guid.NewGuid();
        var response = await CallAsync(() => client.GetSourceAsync(traceId, fragmentId, ct));

        return new AppKnowledge.SourceFragment(
            response.Document_id, response.Title, response.Version, response.Page, response.Anchor, response.Text, response.Snapshot_id);
    }

    private static Generated.Entity ToGeneratedEntity(string value) =>
        new() { Type = "text", Value = value, Provenance = Generated.EntityProvenance.Unknown };

    /// <summary>
    /// Single funnel from any failed `knowledge` call to <see cref="AppKnowledge.KnowledgeFailureException"/>
    /// (knowledge-v0.md §3) — the orchestrator must never see a raw HTTP/JSON exception.
    /// </summary>
    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
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
        catch (OperationCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            // HttpClient's own timeout, not our caller's cancellation.
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
