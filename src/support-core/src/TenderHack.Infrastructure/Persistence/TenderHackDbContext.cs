using Microsoft.EntityFrameworkCore;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Feedback;

namespace TenderHack.Infrastructure.Persistence;

/// <summary>
/// `api`-owned persistence (architecture.md §8). Migrates and reads only the tables this runtime
/// owns — never `kb_*`/`quality_*`.
/// </summary>
public sealed class TenderHackDbContext(DbContextOptions<TenderHackDbContext> options) : DbContext(options)
{
    public DbSet<Case> Cases => Set<Case>();

    public DbSet<CaseEventEntity> CaseEvents => Set<CaseEventEntity>();

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    public DbSet<Feedback> Feedbacks => Set<Feedback>();

    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();

    public DbSet<IdempotencyKeyEntity> IdempotencyKeys => Set<IdempotencyKeyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenderHackDbContext).Assembly);
    }
}
