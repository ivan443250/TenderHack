using System.Net.ServerSentEvents;
using TenderHack.Api.Contracts;
using TenderHack.Application.Exceptions;
using TenderHack.Application.Ports;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Cases;

namespace TenderHack.Api.Endpoints;

public static class CaseEndpoints
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(700);

    /// <summary>Upper bound for one user message; a support question does not need more, and `knowledge` is not sized for arbitrary bodies.</summary>
    public const int MaxMessageLength = 4000;

    public static void MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v0/cases", async (
            HttpContext context, CreateCaseUseCase useCase, GetCaseSnapshotUseCase getSnapshot, IIdempotencyStore idempotency, CancellationToken ct) =>
        {
            var ownerId = OwnerSession.GetOrCreate(context);
            var key = IdempotencyHelper.GetKey(context);

            var cachedId = await IdempotencyHelper.CheckAsync(idempotency, ownerId, IdempotencyScopes.CreateCase, key, payload: new { }, ct);
            if (cachedId is not null && CaseId.TryParse(cachedId, out var existingId))
            {
                var existing = await getSnapshot.ExecuteAsync(existingId, ownerId, ct);
                return Results.Ok(CaseMapper.ToSnapshot(existing, []));
            }

            var @case = await useCase.ExecuteAsync(ownerId, ct);
            if (key is not null)
            {
                await idempotency.SaveAsync(ownerId, IdempotencyScopes.CreateCase, key, IdempotencyHelper.HashPayload(new { }), @case.Id.ToString(), ct);
            }

            return Results.Created($"/api/v0/cases/{@case.Id}", CaseMapper.ToSnapshot(@case, []));
        });

        app.MapGet("/api/v0/cases", async (
            HttpContext context, string? status, ListCasesUseCase useCase, INotificationReader notifications, CancellationToken ct) =>
        {
            if (OwnerSession.TryGet(context) is not { } ownerId)
            {
                return Results.Ok(Array.Empty<CaseListItemResponse>());
            }

            var archivedOnly = string.Equals(status, "archived", StringComparison.OrdinalIgnoreCase);
            var cases = await useCase.ExecuteAsync(ownerId, archivedOnly, ct);

            // One owner-scoped fetch, grouped client-side, rather than N notification queries — the
            // case list is small at hackathon scale and this keeps `GET /cases` a single round trip.
            var unread = await notifications.ListAsync(ownerId, after: 0, unreadOnly: true, ct);
            var unreadByCaseId = unread.GroupBy(n => n.CaseId).ToDictionary(g => g.Key, g => g.Count());

            return Results.Ok(cases
                .Select(c => CaseMapper.ToListItem(c, unreadByCaseId.GetValueOrDefault(c.Id)))
                .ToArray());
        });

        app.MapGet("/api/v0/cases/{caseId}", async (
            HttpContext context, string caseId, GetCaseSnapshotUseCase useCase, ICaseEventReader events,
            IFeedbackRepository feedbackRepository, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            var @case = await useCase.ExecuteAsync(id, ownerId, ct);
            var history = await events.ListAsync(id, after: 0, ct);
            var feedback = @case.FeedbackId is not null ? await feedbackRepository.FindByCaseIdAsync(id, ct) : null;
            return Results.Ok(CaseMapper.ToSnapshot(@case, history, feedback));
        });

        app.MapPost("/api/v0/cases/{caseId}/messages", async (
            HttpContext context, string caseId, SendMessageRequest request, SendMessageUseCase useCase, IIdempotencyStore idempotency, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["Text is required."] });
            }

            if (request.Text.Length > MaxMessageLength)
            {
                // Boundary input is bounded before it reaches any stage: an unbounded body goes straight
                // into `knowledge.understand`, and a few hundred KB of text is enough to stall it.
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = [$"Text must be at most {MaxMessageLength} characters."] });
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);

            // architecture.md §7: "same key + same payload → same logical result". A lost response
            // must not re-run the pipeline (a second `knowledge` round trip, a second moderation
            // check, a second superseded revision) — replay returns the exact original response
            // rather than the case's current state, which the client already has other ways to read
            // (GET /cases/{id}, the event stream).
            if (await CaseEndpointHelpers.TryReplaySendMessageAsync(context, ownerId, request, idempotency, ct) is { } cachedResponse)
            {
                return Results.Ok(cachedResponse);
            }

            var outcome = await useCase.ExecuteAsync(id, ownerId, request.Text, ct);
            var response = CaseMapper.ToSendMessageResponse(caseId, outcome);

            if (IdempotencyHelper.GetKey(context) is { } key)
            {
                await idempotency.SaveAsync(ownerId, IdempotencyScopes.SendMessage, key, IdempotencyHelper.HashPayload(request), IdempotencyHelper.SerializeCachedResponse(response), ct);
            }

            // architecture.md §18: technical trace for judges/team, not end-user UI (web-api-v0.md §1) — a
            // support/debug conversation can ask "what was this turn's trace_id" and grep the logs.
            context.Response.Headers["X-Trace-Id"] = outcome.TraceId.ToString();
            return Results.Ok(response);
        });

        app.MapDelete("/api/v0/cases/{caseId}", async (
            HttpContext context, string caseId, HideCaseUseCase useCase, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            await useCase.ExecuteAsync(id, ownerId, ct);
            return Results.NoContent();
        });

        app.MapGet("/api/v0/cases/{caseId}/events", async (
            HttpContext context, string caseId, long? after, GetCaseSnapshotUseCase ownershipCheck, ICaseEventReader events, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            await ownershipCheck.ExecuteAsync(id, ownerId, ct); // throws CaseNotFoundException when not owned
            var history = await events.ListAsync(id, after ?? 0, ct);
            return Results.Ok(history.Select(CaseMapper.ToTimelineItem).ToArray());
        });

        app.MapGet("/api/v0/cases/{caseId}/events/stream", async (
            HttpContext context, string caseId, GetCaseSnapshotUseCase ownershipCheck, ICaseEventReader events, CancellationToken ct) =>
        {
            if (!CaseId.TryParse(caseId, out var id))
            {
                return Results.BadRequest();
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            await ownershipCheck.ExecuteAsync(id, ownerId, ct);

            var afterEventId = long.TryParse(context.Request.Headers["Last-Event-ID"], out var lastEventId) ? lastEventId : 0;

            // No `eventType:` argument on purpose: the SseItem overload has none, and passing one
            // silently selects the plain-`T` overload, which then JSON-serializes the whole SseItem
            // (id and event name included) as `data:` — the envelope below is what web-api-v0 §6 defines.
            return TypedResults.ServerSentEvents(StreamEventsAsync(id, afterEventId, events, ct));
        });
    }

    private static async IAsyncEnumerable<SseItem<CaseEventResponse>> StreamEventsAsync(
        CaseId caseId, long afterEventId, ICaseEventReader events, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var batch = await events.ListAsync(caseId, afterEventId, ct);
            foreach (var e in batch)
            {
                afterEventId = e.EventId;
                yield return new SseItem<CaseEventResponse>(CaseMapper.ToCaseEvent(caseId, e), "case_event")
                {
                    EventId = e.EventId.ToString(),
                };
            }

            await Task.Delay(PollInterval, ct);
        }
    }
}
