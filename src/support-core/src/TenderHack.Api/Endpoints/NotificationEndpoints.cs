using System.Net.ServerSentEvents;
using TenderHack.Api.Contracts;
using TenderHack.Application.Ports;
using TenderHack.Application.UseCases;

namespace TenderHack.Api.Endpoints;

/// <summary>Owner-scoped, independent of any one case (architecture.md §5.9, web-api-v0.md §12).</summary>
public static class NotificationEndpoints
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(700);

    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v0/notifications", async (
            HttpContext context, long? after, bool? unread, ListNotificationsUseCase useCase, CancellationToken ct) =>
        {
            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            var notifications = await useCase.ExecuteAsync(ownerId, after ?? 0, unread ?? false, ct);
            return Results.Ok(notifications.Select(ToResponse).ToArray());
        });

        app.MapPost("/api/v0/notifications/ack", async (
            HttpContext context, AckNotificationsRequest request, AckNotificationsUseCase useCase, CancellationToken ct) =>
        {
            var ownerId = CaseEndpointHelpers.RequireOwner(context);

            if (!TryParseIds(request.Ids, out var ids))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["ids"] = ["Each id must be a numeric notification_id."] });
            }

            await useCase.ExecuteAsync(ownerId, ids, ct);
            return Results.Ok(new { status = "ok" });
        });

        app.MapGet("/api/v0/notifications/stream", (HttpContext context, INotificationReader reader, CancellationToken ct) =>
        {
            var ownerId = CaseEndpointHelpers.RequireOwner(context);
            var afterId = long.TryParse(context.Request.Headers["Last-Event-ID"], out var lastEventId) ? lastEventId : 0;

            // See CaseEndpoints: the SseItem overload takes no `eventType`, the item carries it.
            return TypedResults.ServerSentEvents(StreamNotificationsAsync(ownerId, afterId, reader, ct));
        });
    }

    /// <summary>Ids are opaque strings on the wire (web-api-v0 §12) but must parse — a bad one is the caller's 400, not our 500.</summary>
    private static bool TryParseIds(IReadOnlyList<string>? raw, out long[] ids)
    {
        ids = [];
        if (raw is null)
        {
            return false;
        }

        var parsed = new long[raw.Count];
        for (var i = 0; i < raw.Count; i++)
        {
            if (!long.TryParse(raw[i], out parsed[i]))
            {
                return false;
            }
        }

        ids = parsed;
        return true;
    }

    private static async IAsyncEnumerable<SseItem<NotificationResponse>> StreamNotificationsAsync(
        string ownerId, long afterId, INotificationReader reader, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var batch = await reader.ListAsync(ownerId, afterId, unreadOnly: false, ct);
            foreach (var n in batch)
            {
                afterId = n.NotificationId;
                yield return new SseItem<NotificationResponse>(ToResponse(n), "notification")
                {
                    EventId = n.NotificationId.ToString(),
                };
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    private static NotificationResponse ToResponse(NotificationEntry n) =>
        new(n.NotificationId.ToString(), n.CaseId.ToString(), n.Type, n.OccurredAt, n.ReadAt,
            new NotificationPayload(n.Title, n.Body, n.IntegrationMode));
}
