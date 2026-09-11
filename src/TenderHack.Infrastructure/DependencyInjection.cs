using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using TenderHack.Application.Abstractions;
using TenderHack.Application.Analytics;
using TenderHack.Infrastructure.Ml;
using TenderHack.Infrastructure.Persistence;
using TenderHack.Infrastructure.Persistence.Repositories;
using TenderHack.Infrastructure.Services;

namespace TenderHack.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(config.GetConnectionString("Postgres")));

        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddMemoryCache();
        services.AddSingleton<IAnalyticsCache, MemoryAnalyticsCache>();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.Configure<ThresholdOptions>(config.GetSection(ThresholdOptions.SectionName));
        services.AddSingleton<OptionsThresholdProvider>();
        services.AddSingleton<IThresholdProvider>(sp => sp.GetRequiredService<OptionsThresholdProvider>());
        services.AddSingleton<IThresholdStore>(sp => sp.GetRequiredService<OptionsThresholdProvider>());

        services.AddHttpClient<IMlService, MlServiceClient>(client =>
            {
                client.BaseAddress = new Uri(config["Ml:BaseUrl"] ?? "http://ml:8000");
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            .AddPolicyHandler(GetRetryPolicy());

        services.AddHostedService<AnalyticsRefreshService>();

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(2, attempt => TimeSpan.FromMilliseconds(200 * attempt));
}
