using System.Reflection;
using Microsoft.Extensions.Options;

namespace TenderHack.Api.Admin;

/// <summary>
/// `/admin` — a self-contained test/demo panel served by `api` itself (same process, same DB), with no
/// link from the SPA and no dependency on it: the page is an embedded resource, its JSON comes from
/// `/admin/api/summary`. Deliberately outside `web-api-v0` (browser ↔ api product contract) — it is
/// a team diagnostic over persisted facts, not a product feature, and must never be reachable through
/// the owner cookie alone (see <see cref="AdminAccess"/>).
/// </summary>
public static class AdminEndpoints
{
    private const string PanelResource = "TenderHack.Api.Admin.panel.html";
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(366);

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin", (HttpContext context, IOptions<AdminOptions> options) =>
        {
            if (Gate(context, options.Value) is { } denied)
            {
                return denied;
            }

            return Results.Content(LoadPanel(), "text/html; charset=utf-8");
        });

        app.MapGet("/admin/api/summary", async (
            HttpContext context, DateTimeOffset? from, DateTimeOffset? to, int? limit,
            IOptions<AdminOptions> options, AdminSummaryQuery query, TimeProvider clock, CancellationToken ct) =>
        {
            if (Gate(context, options.Value) is { } denied)
            {
                return denied;
            }

            var now = clock.GetUtcNow();
            var effectiveTo = to ?? now;
            var effectiveFrom = from ?? effectiveTo - DefaultWindow;
            if (effectiveFrom >= effectiveTo)
            {
                return Results.BadRequest(new { code = "VALIDATION_ERROR", message = "`from` must be earlier than `to`." });
            }

            if (effectiveTo - effectiveFrom > MaxWindow)
            {
                return Results.BadRequest(new { code = "VALIDATION_ERROR", message = $"Window must not exceed {MaxWindow.TotalDays:0} days." });
            }

            var effectiveLimit = Math.Clamp(limit ?? AdminSummaryQuery.DefaultLimit, 1, AdminSummaryQuery.MaxLimit);
            var summary = await query.ExecuteAsync(new AdminSummaryRequest(effectiveFrom, effectiveTo, effectiveLimit), ct);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(summary);
        });
    }

    private static IResult? Gate(HttpContext context, AdminOptions options) =>
        AdminAccess.Check(options, AdminAccess.PresentedToken(context.Request)) switch
        {
            AdminAccessResult.Ok => null,
            AdminAccessResult.Disabled => Results.NotFound(new { code = "ADMIN_DISABLED", message = "Admin panel is disabled (Admin:Enabled=false)." }),
            _ => Results.Json(new { code = "ADMIN_UNAUTHORIZED", message = $"Provide the admin token via `{AdminAccess.TokenHeader}` header or `?{AdminAccess.TokenQuery}=`." }, statusCode: StatusCodes.Status401Unauthorized),
        };

    private static string LoadPanel()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PanelResource)
            ?? throw new InvalidOperationException($"Embedded resource {PanelResource} is missing — check TenderHack.Api.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
