using TenderHack.Api.Contracts;
using TenderHack.Application.Ports;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Cases;

namespace TenderHack.Api.Endpoints;

file static class HandoffEndpointHelpers
{
    /// <summary>
    /// Confirm/retry replay: a repeated call with the same Idempotency-Key returns the current case
    /// snapshot instead of re-running the use-case (which would otherwise hit
    /// `InvalidHandoffTransitionException` — the handoff already moved past `NotRequested`/`Failed`).
    /// </summary>
    public static async Task<IResult?> TryReplayAsync(
        HttpContext context, CaseId id, string ownerId, string scope, object payload,
        IIdempotencyStore idempotency, GetCaseSnapshotUseCase getSnapshot, ICaseEventReader events, CancellationToken ct)
    {
        var key = IdempotencyHelper.GetKey(context);
        var cachedId = await IdempotencyHelper.CheckAsync(idempotency, ownerId, scope, key, payload, ct);
        if (cachedId is null)
        {
            return null;
        }

        var existing = await getSnapshot.ExecuteAsync(id, ownerId, ct);
        var history = await events.ListAsync(id, after: 0, ct);
        return Results.Ok(CaseMapper.ToSnapshot(existing, history));
    }
}

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
            ConfirmHandoffUseCase useCase, GetCaseSnapshotUseCase getSnapshot, IIdempotencyStore idempotency, ICaseEventReader events, CancellationToken ct) =>
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

            if (await HandoffEndpointHelpers.TryReplayAsync(context, id, ownerId, IdempotencyScopes.ConfirmHandoff, request, idempotency, getSnapshot, events, ct) is { } replay)
            {
                return replay;
            }

            // Routing/context/sources are already on `Handoff.Package`, assembled once at `prepare`
            // time (A6) — `summary` is the only field the client ever gets to edit.
            var @case = await useCase.ExecuteAsync(id, ownerId, request.Summary, ct);

            if (IdempotencyHelper.GetKey(context) is { } key)
            {
                await idempotency.SaveAsync(ownerId, IdempotencyScopes.ConfirmHandoff, key, IdempotencyHelper.HashPayload(request), id.ToString(), ct);
            }

            var history = await events.ListAsync(id, after: 0, ct);
            return Results.Ok(CaseMapper.ToSnapshot(@case, history));
        });

        app.MapPost("/api/v0/cases/{caseId}/handoff/retry", async (
            HttpContext context, string caseId, RetryHandoffRequest request,
            RetryHandoffUseCase useCase, GetCaseSnapshotUseCase getSnapshot, IIdempotencyStore idempotency, ICaseEventReader events, CancellationToken ct) =>
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

            if (await HandoffEndpointHelpers.TryReplayAsync(context, id, ownerId, IdempotencyScopes.RetryHandoff, request, idempotency, getSnapshot, events, ct) is { } replay)
            {
                return replay;
            }

            var @case = await useCase.ExecuteAsync(id, ownerId, request.Summary, ct);

            if (IdempotencyHelper.GetKey(context) is { } key)
            {
                await idempotency.SaveAsync(ownerId, IdempotencyScopes.RetryHandoff, key, IdempotencyHelper.HashPayload(request), id.ToString(), ct);
            }

            var history = await events.ListAsync(id, after: 0, ct);
            return Results.Ok(CaseMapper.ToSnapshot(@case, history));
        });
    }
}
