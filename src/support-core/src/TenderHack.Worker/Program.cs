using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenderHack.Infrastructure;
using TenderHack.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSupportCoreInfrastructure(builder.Configuration);
builder.Services.AddHostedService<HandoffSubmitWorker>();
builder.Services.AddHostedService<HandoffStatusSyncWorker>();

await builder.Build().RunAsync();
