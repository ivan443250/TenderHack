using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TenderHack.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef migrations add` build the model without a running `TenderHack.Api`/`Worker` host.
/// The connection string here is never used to actually connect — migration generation only needs
/// the model, not a live database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TenderHackDbContext>
{
    public TenderHackDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TenderHackDbContext>()
            .UseNpgsql("Host=localhost;Database=tenderhack_design;Username=design;Password=design")
            .UseSnakeCaseNamingConvention();

        return new TenderHackDbContext(optionsBuilder.Options);
    }
}
