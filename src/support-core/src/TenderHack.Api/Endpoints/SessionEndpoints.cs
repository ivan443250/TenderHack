namespace TenderHack.Api.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v0/session", (HttpContext context) =>
        {
            OwnerSession.GetOrCreate(context);
            return Results.Ok(new { status = "ok" });
        });
    }
}
