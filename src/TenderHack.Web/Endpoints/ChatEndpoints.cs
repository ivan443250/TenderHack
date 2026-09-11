using TenderHack.Application.Tickets;

namespace TenderHack.Web.Endpoints;

// Text-in/text-out fallback for clients that cannot use SignalR (curl, demo scripts, tests).
// Normal chat traffic goes through ChatHub.
public sealed class ChatEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat");

        group.MapPost("/messages", async (SendMessageRequest body, ProcessUserMessageHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ProcessUserMessageCommand(body.UserId, body.Text), ct);
            return Results.Ok(result);
        });
    }

    public sealed record SendMessageRequest(Guid UserId, string Text);
}
