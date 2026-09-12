using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenderHack.Application.Orchestration;
using TenderHack.Application.UseCases;
using TenderHack.Infrastructure;
using TenderHack.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSupportCoreInfrastructure(builder.Configuration);

// Same reasoning as the Api's composition root: TenderHack.Application stays framework-free, so
// the use-case the poll worker drives is wired here.
builder.Services.AddScoped<CaseCompletionPublisher>();
builder.Services.AddScoped<HandoffSubmissionPublisher>();
builder.Services.AddScoped<IngestHandoffStatusUseCase>();
builder.Services.AddScoped<CleanUpStaleTurnsUseCase>();

builder.Services.Configure<StaleTurnCleanupOptions>(builder.Configuration.GetSection(StaleTurnCleanupOptions.SectionName));

builder.Services.AddHostedService<HandoffSubmitWorker>();
builder.Services.AddHostedService<HandoffStatusSyncWorker>();
builder.Services.AddHostedService<QualityPushWorker>();
builder.Services.AddHostedService<StaleTurnCleanupWorker>();

await builder.Build().RunAsync();
