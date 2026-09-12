using TenderHack.Api.Contracts;
using TenderHack.Application.Exceptions;
using TenderHack.Application.Knowledge;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Api.Endpoints;

/// <summary>
/// Read-only proxy to `knowledge`'s quality endpoints (architecture.md §5.7) — no aggregation logic
/// here, that is entirely `knowledge`'s job; this layer only authorizes and reshapes.
/// </summary>
public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v0/analytics/evaluations", async (
            HttpContext context, string case_id, IKnowledgeService knowledge, ICaseRepository cases, CancellationToken ct) =>
        {
            if (!CaseId.TryParse(case_id, out var id))
            {
                return Results.BadRequest(new { code = "VALIDATION_ERROR", message = "Invalid case_id." });
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            var @case = await cases.FindAsync(id, ct) ?? throw new CaseNotFoundException(id);
            if (@case.OwnerId != ownerId)
            {
                throw new CaseNotFoundException(id);
            }

            var evaluations = await knowledge.GetQualityEvaluationsAsync(case_id, ct);
            return Results.Ok(ToResponse(evaluations));
        });

        // No accounts/roles exist in P0 (hackathon-requirements.md §3) — this is gated by "has an
        // owner session" only, not a real analyst/admin role. It is a cross-case aggregate view by
        // design, so per-case owner-scoping does not apply to it.
        app.MapGet("/api/v0/analytics/issue-groups", async (HttpContext context, IKnowledgeService knowledge, CancellationToken ct) =>
        {
            CaseEndpointHelpers.RequireOwner(context);
            var groups = await knowledge.GetIssueGroupsAsync(ct);
            return Results.Ok(ToResponse(groups));
        });
    }

    private static QualityEvaluationsResponse ToResponse(QualityEvaluations evaluations) =>
        new(evaluations.CaseId, [.. evaluations.Evaluations.Select(ToView)]);

    private static EvaluationView ToView(Evaluation e) => new(
        e.TurnId, ToView(e.FactualSupport), ToView(e.Completeness), ToView(e.Clarity), ToView(e.NextStep),
        e.CriticalError, e.CriticalErrorReason, e.EvaluatedAt);

    private static DimensionScoreView ToView(DimensionScore s) =>
        new(s.Value, s.Evidence, s.Reason, s.Limitation, s.ImprovementSuggestion);

    private static IssueGroupsResponse ToResponse(IssueGroups groups) =>
        new([.. groups.Groups.Select(ToView)]);

    private static IssueGroupView ToView(IssueGroup g) => new(
        g.GroupId, g.Label, g.N, g.RepresentativeCaseIds, g.NegativeSignalCount, g.UnresolvedCount,
        g.Limitations, [.. g.Hypotheses.Select(h => new HypothesisView(h.Type, h.Text, h.Confidence))]);
}
