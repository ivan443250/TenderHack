using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using TenderHack.Infrastructure.Persistence;
using Xunit;

namespace TenderHack.Api.Tests;

/// <summary>
/// Every other suite migrates an empty database, which is exactly the case that cannot catch a
/// migration that only breaks on pre-existing rows. This one stops one migration short of the
/// point where `notifications` gained its unique `(owner_id, case_id, type, source_event_id)`
/// index, seeds the row shape a real deployment accumulates (several `HANDOFF_UPDATED`
/// notifications for one case from staged status updates), and only then migrates to the latest
/// schema — the same path `docker compose up --build` takes against a database with demo history.
/// </summary>
public sealed class MigrationUpgradeTests : IAsyncLifetime
{
    private const string LastMigrationBeforeSourceEventId = "20260912203956_AddHandoffPackage";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tenderhack_upgrade_test")
        .WithUsername("api_rw")
        .WithPassword("test_only")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task MigratingOverExistingDuplicateNotificationsSucceedsAndKeepsThemDistinct()
    {
        await using var db = NewDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(LastMigrationBeforeSourceEventId);

        var caseId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO notifications (owner_id, case_id, type, occurred_at, read_at, title, body, integration_mode)
                VALUES ('owner-1', {caseId}, 'HANDOFF_UPDATED', now(), NULL, 'Обновление по обращению', 'stage {i}', 'SIMULATED')
                """);
        }

        await migrator.MigrateAsync();

        var sourceEventIds = await db.Set<NotificationEntity>()
            .Where(n => n.CaseId == caseId)
            .Select(n => n.SourceEventId)
            .ToListAsync();
        Assert.Equal(3, sourceEventIds.Count);
        Assert.Equal(3, sourceEventIds.Distinct().Count());
        // Backfilled ids must stay out of the range real case_events ids occupy.
        Assert.All(sourceEventIds, id => Assert.True(id < 0));
    }

    private TenderHackDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<TenderHackDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new TenderHackDbContext(options);
    }
}
