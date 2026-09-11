using TenderHack.Application.Analytics;

namespace TenderHack.Web.Endpoints;

public sealed class AnalyticsEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").WithTags("Analytics");

        group.MapGet("/", async (GetAnalyticsHandler handler, CancellationToken ct) =>
        {
            var snapshot = await handler.HandleAsync(ct);
            return snapshot is null ? Results.NoContent() : Results.Ok(snapshot);
        });
    }
}
