using TenderHack.Application.Analytics;

namespace TenderHack.Web.Endpoints;

// Lets the team retune the escalation threshold live during the demo — no redeploy (section 8.1).
public sealed class AdminEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin");

        group.MapPost("/threshold", async (UpdateThresholdRequest body, UpdateThresholdHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UpdateThresholdCommand(body.MinConfidence), ct);
            return Results.NoContent();
        });
    }

    public sealed record UpdateThresholdRequest(double MinConfidence);
}
