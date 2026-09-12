using TenderHack.Api.Contracts;
using TenderHack.Application.Ports;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Cases;

namespace TenderHack.Api.Endpoints;

public static class HandoffEndpoints
{
    public static void MapHandoffEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v0/cases/{caseId}/handoff/prepare", async (
            HttpContext context, string caseId, PrepareHandoffUseCase useCase, ICaseEventReader events, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            var @case = await useCase.ExecuteAsync(id, ownerId, ct);
            var history = await events.ListAsync(id, after: 0, ct);
            return Results.Ok(CaseMapper.ToSnapshot(@case, history));
        });

        app.MapPost("/api/v0/cases/{caseId}/handoff/confirm", async (
            HttpContext context, string caseId, ConfirmHandoffRequest request,
            ConfirmHandoffUseCase useCase, ICaseEventReader events, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            if (string.IsNullOrWhiteSpace(request.Summary))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["summary"] = ["Summary is required."] });
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            var history = await events.ListAsync(id, after: 0, ct);
            var routing = CaseMapper.ExtractLastRouting(history)
                ?? new RoutingFacts("l1-general", [], EngineeringReviewSuggested: false);

            var @case = await useCase.ExecuteAsync(
                id, ownerId, request.Summary, routing.DispatchQueue, routing.ReasonCodes, routing.EngineeringReviewSuggested, ct);
            history = await events.ListAsync(id, after: 0, ct);
            return Results.Ok(CaseMapper.ToSnapshot(@case, history));
        });

        app.MapPost("/api/v0/cases/{caseId}/handoff/retry", async (
            HttpContext context, string caseId, RetryHandoffRequest request,
            RetryHandoffUseCase useCase, ICaseEventReader events, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            if (string.IsNullOrWhiteSpace(request.Summary))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["summary"] = ["Summary is required."] });
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            var history = await events.ListAsync(id, after: 0, ct);
            var routing = CaseMapper.ExtractLastRouting(history)
                ?? new RoutingFacts("l1-general", [], EngineeringReviewSuggested: false);

            var @case = await useCase.ExecuteAsync(
                id, ownerId, request.Summary, routing.DispatchQueue, routing.ReasonCodes, routing.EngineeringReviewSuggested, ct);
            history = await events.ListAsync(id, after: 0, ct);
            return Results.Ok(CaseMapper.ToSnapshot(@case, history));
        });
    }
}
