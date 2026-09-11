using Microsoft.EntityFrameworkCore;
using TenderHack.Domain.Entities;

namespace TenderHack.Infrastructure.Persistence;

// Owns the domain tables only (tickets, messages, message_analysis, feedback, specialists).
// kb_* tables belong to the Python side (ingest) and are never mapped here — see section 6.1.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Specialist> Specialists => Set<Specialist>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
