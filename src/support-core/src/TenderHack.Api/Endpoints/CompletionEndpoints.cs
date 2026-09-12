using TenderHack.Api.Contracts;
using TenderHack.Application.Ports;
using TenderHack.Application.UseCases;

namespace TenderHack.Api.Endpoints;

public static class CompletionEndpoints
{
    public static void MapCompletionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v0/cases/{caseId}/complete", async (
            HttpContext context, string caseId, CompleteCaseRequest request,
            CompleteCaseUseCase useCase, ICaseEventReader events, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            var @case = await useCase.ExecuteAsync(id, ownerId, request.Solved, ct);
            var history = await events.ListAsync(id, after: 0, ct);
            return Results.Ok(CaseMapper.ToSnapshot(@case, history));
        });

        app.MapPost("/api/v0/cases/{caseId}/feedback", async (
            HttpContext context, string caseId, SubmitFeedbackRequest request,
            SubmitFeedbackUseCase useCase, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            var feedback = await useCase.ExecuteAsync(
                id, ownerId, request.SpecialistRating, request.InformationQualityRating, request.Solved, request.CommentText, ct);

            return Results.Ok(new FeedbackView(
                feedback.SpecialistRating, feedback.InformationQualityRating, feedback.Solved, feedback.CommentText, feedback.SubmittedAt));
        });
    }
}
