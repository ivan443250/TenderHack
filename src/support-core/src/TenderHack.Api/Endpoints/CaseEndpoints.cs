using System.Net.ServerSentEvents;
using System.Text.Json;
using TenderHack.Api.Contracts;
using TenderHack.Application.Exceptions;
using TenderHack.Application.Ports;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Cases;

namespace TenderHack.Api.Endpoints;

public static class CaseEndpoints
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(700);

    public static void MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v0/cases", async (HttpContext context, CreateCaseUseCase useCase, CancellationToken ct) =>
        {
            var ownerId = OwnerSession.GetOrCreate(context);
            var @case = await useCase.ExecuteAsync(ownerId, ct);
            return Results.Created($"/api/v0/cases/{@case.Id}", CaseMapper.ToSnapshot(@case, []));
        });

        app.MapGet("/api/v0/cases", async (HttpContext context, string? status, ListCasesUseCase useCase, CancellationToken ct) =>
        {
            if (OwnerSession.TryGet(context) is not { } ownerId)
            {
                return Results.Ok(Array.Empty<CaseListItemResponse>());
            }

            var archivedOnly = string.Equals(status, "archived", StringComparison.OrdinalIgnoreCase);
            var cases = await useCase.ExecuteAsync(ownerId, archivedOnly, ct);
            return Results.Ok(cases.Select(CaseMapper.ToListItem).ToArray());
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
            HttpContext context, string caseId, SendMessageRequest request, SendMessageUseCase useCase, CancellationToken ct) =>
        {
            if (!CaseEndpointHelpers.TryRequireCaseId(caseId, out var id, out var badRequest))
            {
                return badRequest;
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["Text is required."] });
            }

            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            var outcome = await useCase.ExecuteAsync(id, ownerId, request.Text, ct);
            return Results.Ok(CaseMapper.ToSendMessageResponse(caseId, outcome));
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
            return TypedResults.ServerSentEvents(StreamEventsAsync(id, afterEventId, events, ct), eventType: "case_event");
        });
    }

    private static async IAsyncEnumerable<SseItem<string>> StreamEventsAsync(
        CaseId caseId, long afterEventId, ICaseEventReader events, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var batch = await events.ListAsync(caseId, afterEventId, ct);
            foreach (var e in batch)
            {
                afterEventId = e.EventId;
                yield return new SseItem<string>(JsonSerializer.Serialize(CaseMapper.ToTimelineItem(e)), "case_event")
                {
                    EventId = e.EventId.ToString(),
                };
            }

            await Task.Delay(PollInterval, ct);
        }
    }

}
