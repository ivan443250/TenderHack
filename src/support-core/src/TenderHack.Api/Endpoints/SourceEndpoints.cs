using TenderHack.Api.Contracts;
using TenderHack.Application.Knowledge;

namespace TenderHack.Api.Endpoints;

public static class SourceEndpoints
{
    public static void MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v0/sources/{fragmentId}", async (string fragmentId, IKnowledgeService knowledge, CancellationToken ct) =>
        {
            var source = await knowledge.GetSourceAsync(fragmentId, ct);
            return Results.Ok(new SourceResponse(
                source.DocumentId, source.Title, source.Version, source.Page, source.Anchor, source.Text, source.SnapshotId));
        });
    }
}
