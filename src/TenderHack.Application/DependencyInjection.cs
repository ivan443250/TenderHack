using Microsoft.Extensions.DependencyInjection;
using TenderHack.Application.Analytics;
using TenderHack.Application.Tickets;

namespace TenderHack.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ProcessUserMessageHandler>();
        services.AddScoped<TakeTicketHandler>();
        services.AddScoped<SendOperatorMessageHandler>();
        services.AddScoped<ResolveTicketHandler>();
        services.AddScoped<SubmitFeedbackHandler>();
        services.AddScoped<GetOperatorQueueHandler>();
        services.AddScoped<GetTicketHandler>();
        services.AddScoped<GetAnalyticsHandler>();
        services.AddScoped<UpdateThresholdHandler>();

        return services;
    }
}
