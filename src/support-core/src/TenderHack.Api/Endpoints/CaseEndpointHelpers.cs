using TenderHack.Api.Contracts;
using TenderHack.Application.Exceptions;
using TenderHack.Application.Ports;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Cases;

namespace TenderHack.Api.Endpoints;

internal static class CaseEndpointHelpers
{
    public static string RequireOwner(HttpContext context) =>
        OwnerSession.TryGet(context) ?? throw new UnauthenticatedException();

    public static bool TryRequireCaseId(string raw, out CaseId id, out IResult badRequest)
    {
        if (CaseId.TryParse(raw, out id))
        {
            badRequest = Results.Empty;
            return true;
        }

        badRequest = Results.BadRequest(new { code = "VALIDATION_ERROR", message = "Invalid case id." });
        return false;
    }

    /// <summary>
    /// `POST /messages` replay (architecture.md §7): unlike other idempotent endpoints, there is no
    /// stable entity to re-fetch a matching response from — the pipeline's exact output (decision,
    /// answer markdown, sources) lives only in the response itself, so the response is what
    /// <see cref="IIdempotencyStore"/>'s opaque `EntityId` carries here, JSON-serialized.
    /// </summary>
    public static async Task<SendMessageResponse?> TryReplaySendMessageAsync(
        HttpContext context, string ownerId, SendMessageRequest request, IIdempotencyStore idempotency, CancellationToken ct)
    {
        var key = IdempotencyHelper.GetKey(context);
        var cached = await IdempotencyHelper.CheckAsync(idempotency, ownerId, IdempotencyScopes.SendMessage, key, request, ct);
        return cached is null ? null : IdempotencyHelper.DeserializeCachedResponse<SendMessageResponse>(cached);
    }

    /// <summary>
    /// Generic "replay returns the current case snapshot" pattern (used by confirm/retry handoff and
    /// complete): valid whenever the operation is naturally idempotent at the state level — a second
    /// call converges to the same terminal state the first one already reached, so re-reading it is
    /// a correct replay, not just an approximation.
    /// </summary>
    public static async Task<IResult?> TryReplayWithSnapshotAsync(
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
