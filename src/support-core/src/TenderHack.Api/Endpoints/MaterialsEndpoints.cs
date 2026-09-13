using TenderHack.Api.Contracts;
using TenderHack.Application.Knowledge;

namespace TenderHack.Api.Endpoints;

/// <summary>E3 (docs/plans/active/2026-09-demo-readiness.md): read-only proxy to `knowledge`'s
/// materials endpoints for the "Материалы" tab (web-api-v0.md §15) — no Decision fields, owner
/// session required like <see cref="SourceEndpoints"/>, content is static for a given snapshot so a
/// short client-side cache is safe.</summary>
public static class MaterialsEndpoints
{
    public static void MapMaterialsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v0/materials", async (HttpContext context, IKnowledgeService knowledge, CancellationToken ct) =>
        {
            CaseEndpointHelpers.RequireOwner(context);

            var materials = await knowledge.ListMaterialsAsync(ct);
            context.Response.Headers.CacheControl = "private, max-age=300";
            return Results.Ok(new MaterialsResponse(
                materials.SnapshotId,
                [.. materials.Items.Select(m => new MaterialSummaryResponse(m.DocumentId, m.Title, m.DeclaredVersion, m.DeclaredDate, m.PageCount, m.FragmentCount))]));
        });

        app.MapGet("/api/v0/materials/{documentId}/sections", async (HttpContext context, string documentId, IKnowledgeService knowledge, CancellationToken ct) =>
        {
            CaseEndpointHelpers.RequireOwner(context);

            var sections = await knowledge.ListMaterialSectionsAsync(documentId, ct);
            context.Response.Headers.CacheControl = "private, max-age=300";
            return Results.Ok(new MaterialSectionsResponse(
                sections.SnapshotId,
                sections.DocumentId,
                [.. sections.Items.Select(s => new MaterialSectionResponse(s.Section, s.PageStart, s.PageEnd, s.FirstFragmentId))]));
        });
    }
}
