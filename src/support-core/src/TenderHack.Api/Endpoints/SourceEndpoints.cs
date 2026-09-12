using TenderHack.Api.Contracts;
using TenderHack.Application.Knowledge;

namespace TenderHack.Api.Endpoints;

public static class SourceEndpoints
{
    public static void MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v0/sources/{fragmentId}", async (HttpContext context, string fragmentId, IKnowledgeService knowledge, CancellationToken ct) =>
        {
            // web-api-v0.md §11: case/source/analytics/notification access is authorized
            // server-side by owner_id — an anonymous caller with no session must not be able to
            // enumerate the normative corpus through this proxy.
            CaseEndpointHelpers.RequireOwner(context);

            var source = await knowledge.GetSourceAsync(fragmentId, ct);
            return Results.Ok(new SourceResponse(
                source.DocumentId, source.Title, source.Version, source.Page, source.Anchor, source.Text, source.SnapshotId));
        });
    }
}
