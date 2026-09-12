var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "api" }));
// Foundation scaffold: readiness is process-only until API-owned persistence and
// the generated Knowledge client are wired into the runtime.
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", service = "api", dependencies = "not_checked" }));

app.Run();

public partial class Program { }
