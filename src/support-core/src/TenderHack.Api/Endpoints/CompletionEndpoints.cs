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
            CompleteCaseUseCase useCase, GetCaseSnapshotUseCase getSnapshot, IIdempotencyStore idempotency, ICaseEventReader events, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);

            // architecture.md §7: a retried `complete` must not surface `CASE_CLOSED` — a repeat of
            // the same logical request replays the (already-terminal, so still-accurate) snapshot.
            if (await CaseEndpointHelpers.TryReplayWithSnapshotAsync(context, id, ownerId, IdempotencyScopes.CompleteCase, request, idempotency, getSnapshot, events, ct) is { } replay)
            {
                return replay;
            }

            var @case = await useCase.ExecuteAsync(id, ownerId, request.Solved, ct);

            if (IdempotencyHelper.GetKey(context) is { } key)
            {
                await idempotency.SaveAsync(ownerId, IdempotencyScopes.CompleteCase, key, IdempotencyHelper.HashPayload(request), id.ToString(), ct);
            }

            var history = await events.ListAsync(id, after: 0, ct);
            return Results.Ok(CaseMapper.ToSnapshot(@case, history));
        });

        app.MapPost("/api/v0/cases/{caseId}/feedback", async (
            HttpContext context, string caseId, SubmitFeedbackRequest request,
            SubmitFeedbackUseCase useCase, IFeedbackRepository feedbackRepository, IIdempotencyStore idempotency, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            var key = IdempotencyHelper.GetKey(context);

            // web-api-v0.md §9.3: "the same Idempotency-Key returns the stored one" instead of a 409.
            var cachedId = await IdempotencyHelper.CheckAsync(idempotency, ownerId, IdempotencyScopes.SubmitFeedback, key, request, ct);
            if (cachedId is not null)
            {
                var existing = await feedbackRepository.FindByCaseIdAsync(id, ct);
                return Results.Ok(CaseMapper.ToFeedbackView(existing));
            }

            var feedback = await useCase.ExecuteAsync(
                id, ownerId, request.SpecialistRating, request.InformationQualityRating, request.Solved, request.CommentText, ct);

            if (key is not null)
            {
                await idempotency.SaveAsync(ownerId, IdempotencyScopes.SubmitFeedback, key, IdempotencyHelper.HashPayload(request), feedback.Id.ToString(), ct);
            }

            return Results.Ok(CaseMapper.ToFeedbackView(feedback));
        });
    }
}
