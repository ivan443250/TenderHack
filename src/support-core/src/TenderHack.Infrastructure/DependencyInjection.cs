using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TenderHack.Application.Knowledge;
using TenderHack.Application.Ports;
using TenderHack.Infrastructure.KnowledgeClient;
using TenderHack.Infrastructure.Persistence;
using Generated = TenderHack.Infrastructure.KnowledgeClient.Generated;

namespace TenderHack.Infrastructure;

/// <summary>Composition root for this project's own services. `TenderHack.Api`/`Worker` call this; nothing here reaches back into them.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddSupportCoreInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        // A single pooled factory backs both: CaseEventStore creates its own short-lived contexts
        // from it directly, and the scoped registration below hands the ambient unit-of-work one
        // per request. Registering AddDbContext *and* AddPooledDbContextFactory side by side is not
        // supported — they'd both try to own DbContextOptions<TenderHackDbContext>.
        services.AddPooledDbContextFactory<TenderHackDbContext>(options => ConfigureNpgsql(options, configuration));
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<TenderHackDbContext>>().CreateDbContext());

        services.AddScoped<ICaseRepository, CaseRepository>();
        services.AddScoped<ICaseEventReader, CaseEventReader>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddSingleton<ITurnEventStream, CaseEventStore>();

        services.Configure<KnowledgeServiceOptions>(configuration.GetSection(KnowledgeServiceOptions.SectionName));
        services.AddHttpClient<Generated.IKnowledgeApiClient, Generated.KnowledgeApiClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<KnowledgeServiceOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseAddress);
            client.Timeout = options.Timeout;
        });
        services.AddScoped<IKnowledgeService, HttpKnowledgeService>();

        return services;
    }

    private static void ConfigureNpgsql(DbContextOptionsBuilder options, IConfiguration configuration) =>
        options.UseNpgsql(configuration.GetConnectionString("Postgres"))
               .UseSnakeCaseNamingConvention();
}
