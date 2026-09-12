using System.Text.Json.Serialization;
using TenderHack.Api.Endpoints;
using TenderHack.Api.ExceptionHandling;
using TenderHack.Api.Moderation;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Ports;
using TenderHack.Application.UseCases;
using TenderHack.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSupportCoreInfrastructure(builder.Configuration);

// Application composition: TenderHack.Application stays framework-free, so its use-cases and
// orchestrator are wired here in the outermost layer rather than inside Application itself.
builder.Services.AddSingleton(new ModerationOptions());
builder.Services.AddSingleton<IModerationRuleEngine, PassthroughModerationRuleEngine>();
builder.Services.AddScoped<TurnOrchestrator>();
builder.Services.AddScoped<CreateCaseUseCase>();
builder.Services.AddScoped<SendMessageUseCase>();
builder.Services.AddScoped<GetCaseSnapshotUseCase>();
builder.Services.AddScoped<ListCasesUseCase>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseUpper));
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "api" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", service = "api", dependencies = "not_checked" }));

app.MapSessionEndpoints();
app.MapCaseEndpoints();
app.MapSourceEndpoints();

app.Run();

public partial class Program { }
