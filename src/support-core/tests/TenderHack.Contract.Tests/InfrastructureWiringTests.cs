using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TenderHack.Application.Knowledge;
using TenderHack.Application.Ports;
using TenderHack.Infrastructure;
using TenderHack.Infrastructure.Persistence;
using Xunit;

namespace TenderHack.Contract.Tests;

/// <summary>
/// Proves `AddSupportCoreInfrastructure` wires cleanly and the EF model builds against the Npgsql
/// provider — without opening a real database connection (model building is metadata-only).
/// </summary>
public sealed class InfrastructureWiringTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=tenderhack_test;Username=test;Password=test",
                ["Knowledge:BaseAddress"] = "http://knowledge.invalid",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSupportCoreInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AllApplicationPortsResolve()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICaseRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOutbox>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITurnEventStream>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IKnowledgeService>());
    }

    [Fact]
    public void EfModelBuildsWithoutAConnection()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<TenderHackDbContext>();
        var tableNames = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToArray();

        Assert.Contains("cases", tableNames);
        Assert.Contains("turns", tableNames);
        Assert.Contains("handoffs", tableNames);
        Assert.Contains("case_events", tableNames);
        Assert.Contains("outbox", tableNames);
    }

    [Fact]
    public void TurnRevisionIsUniquePerCase()
    {
        // Regression: two concurrent sends persisted two "revision 3" rows — the `xmin` token on
        // `cases` cannot see an insert into an owned collection, so the index is the real guard.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<TenderHackDbContext>();
        var turns = db.Model.GetEntityTypes().Single(e => e.GetTableName() == "turns");
        var uniqueIndex = turns.GetIndexes().SingleOrDefault(i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["CaseId", "Revision"]));

        Assert.NotNull(uniqueIndex);
    }
}
