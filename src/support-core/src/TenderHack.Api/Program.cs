using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenderHack.Api.Endpoints;
using TenderHack.Api.ExceptionHandling;
using TenderHack.Application.Orchestration;
using TenderHack.Application.UseCases;
using TenderHack.Infrastructure;
using TenderHack.Infrastructure.Handoff;
using TenderHack.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSupportCoreInfrastructure(builder.Configuration);

// Application composition: TenderHack.Application stays framework-free, so its use-cases and
// orchestrator are wired here in the outermost layer rather than inside Application itself.
// IModerationRuleEngine/IKnowledgeService/IHandoffAdapter/etc. are registered by AddSupportCoreInfrastructure above.
builder.Services.AddSingleton(new ModerationOptions());
builder.Services.AddScoped<TurnOrchestrator>();
builder.Services.AddScoped<CreateCaseUseCase>();
builder.Services.AddScoped<SendMessageUseCase>();
builder.Services.AddScoped<GetCaseSnapshotUseCase>();
builder.Services.AddScoped<ListCasesUseCase>();
builder.Services.AddScoped<PrepareHandoffUseCase>();
builder.Services.AddScoped<ConfirmHandoffUseCase>();
builder.Services.AddScoped<RetryHandoffUseCase>();
builder.Services.AddScoped<IngestHandoffStatusUseCase>();
builder.Services.AddScoped<CaseCompletionPublisher>();
builder.Services.AddScoped<CompleteCaseUseCase>();
builder.Services.AddScoped<SubmitFeedbackUseCase>();
builder.Services.AddScoped<ListNotificationsUseCase>();
builder.Services.AddScoped<AckNotificationsUseCase>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseUpper));
});

// A malformed/mistyped JSON body must surface through ApiExceptionHandler as `MALFORMED_REQUEST`
// (web-api-v0 §10: every error carries a machine `code`); without this, minimal APIs answer with an
// empty 400 before the handler ever sees it.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

// `api` owns its schema outright (architecture.md §8) — applying its own migrations on startup is
// the only place that needs to happen; a fresh `docker compose up` must not require a manual step.
// A failed migration should fail fast rather than start the process against a half-built schema.
using (var startupScope = app.Services.CreateScope())
{
    await startupScope.ServiceProvider.GetRequiredService<TenderHackDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();

// Dev-only manual test console (wwwroot/) — same-origin static files so the owner_id cookie works
// without any CORS setup. Not part of the product surface.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "api" }));
app.MapGet("/health/ready", async (TenderHackDbContext db, CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);
    return canConnect
        ? Results.Ok(new { status = "ready", service = "api", dependencies = "ok" })
        : Results.Json(new { status = "not_ready", service = "api", dependencies = "postgres_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapSessionEndpoints();
app.MapCaseEndpoints();
app.MapSourceEndpoints();
app.MapHandoffEndpoints();
app.MapCompletionEndpoints();
app.MapNotificationEndpoints();
app.MapAnalyticsEndpoints();

var supportOptions = app.Services.GetRequiredService<IOptions<SupportOptions>>().Value;
app.MapSupportWebhookEndpoints(supportOptions);

app.Run();

public partial class Program { }
