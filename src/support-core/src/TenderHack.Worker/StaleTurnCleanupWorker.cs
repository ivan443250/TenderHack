using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderHack.Application.UseCases;

namespace TenderHack.Worker;

/// <summary>`stale-turn cleanup` (architecture.md §12) — see <see cref="CleanUpStaleTurnsUseCase"/>.</summary>
public sealed class StaleTurnCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<StaleTurnCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan interval;
            using (var scope = scopeFactory.CreateScope())
            {
                interval = scope.ServiceProvider.GetRequiredService<IOptions<StaleTurnCleanupOptions>>().Value.PollInterval;
            }

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "stale-turn cleanup tick failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<StaleTurnCleanupOptions>>().Value;
        var useCase = scope.ServiceProvider.GetRequiredService<CleanUpStaleTurnsUseCase>();

        var cleaned = await useCase.ExecuteAsync(options.Ttl, ct);
        if (cleaned > 0)
        {
            logger.LogWarning("stale-turn cleanup failed {Count} orphaned turn(s)", cleaned);
        }
    }
}
