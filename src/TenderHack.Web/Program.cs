using Serilog;
using TenderHack.Application;
using TenderHack.Application.Abstractions;
using TenderHack.Infrastructure;
using TenderHack.Web.Endpoints;
using TenderHack.Web.Hubs;
using TenderHack.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext());

builder.Services.AddApplication().AddInfrastructure(builder.Configuration);

builder.Services.AddScoped<IChatNotifier, SignalRChatNotifier>();
builder.Services.AddScoped<IOperatorNotifier, SignalROperatorNotifier>();

builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var endpointGroups = new IEndpointGroup[]
{
    new ChatEndpoints(),
    new TicketEndpoints(),
    new FeedbackEndpoints(),
    new AnalyticsEndpoints(),
    new AdminEndpoints()
};

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors();

app.MapHealthChecks("/health");

foreach (var group in endpointGroups)
{
    group.Map(app);
}

app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<OperatorHub>("/hubs/operator");

app.Run();
