using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderHack.Application.Ports;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Cases;
using TenderHack.Infrastructure.Handoff;

namespace TenderHack.Worker;

/// <summary>
/// `handoff-status-sync` (support-adapter-v0.md §6.1): polls every accepted, non-terminal, non-stale
/// handoff and applies whatever <see cref="IHandoffAdapter.GetStatusAsync"/> reports through the same
/// `IngestHandoffStatusUseCase` write path the inbound webhook uses — so `HANDOFF_STATUS`
/// events/notifications and completion fire identically regardless of channel. A case past
/// `Support:StatusPoll:Ttl` is marked stale and dropped from future polling.
/// </summary>
public sealed class HandoffStatusSyncWorker(IServiceScopeFactory scopeFactory, ILogger<HandoffStatusSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan interval;
            using (var scope = scopeFactory.CreateScope())
            {
                interval = scope.ServiceProvider.GetRequiredService<IOptions<SupportOptions>>().Value.StatusPoll.Initial;
            }

            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "handoff-status-sync tick failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var cases = scope.ServiceProvider.GetRequiredService<ICaseRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var adapter = scope.ServiceProvider.GetRequiredService<IHandoffAdapter>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SupportOptions>>().Value;
        var ingest = scope.ServiceProvider.GetRequiredService<IngestHandoffStatusUseCase>();

        var pending = await cases.ListPendingHandoffPollsAsync(ct);

        foreach (var @case in pending)
        {
            await PollOneAsync(@case, adapter, clock, options, ingest, unitOfWork, ct);
        }
    }

    private async Task PollOneAsync(
        Case @case, IHandoffAdapter adapter, TimeProvider clock, SupportOptions options,
        IngestHandoffStatusUseCase ingest, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var handoff = @case.Handoff!;
        var acceptedAt = handoff.AcceptedAt ?? clock.GetUtcNow();

        if (clock.GetUtcNow() - acceptedAt > options.StatusPoll.Ttl)
        {
            @case.MarkHandoffStale();
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        try
        {
            var query = new HandoffStatusQuery(@case.Id, handoff.Id, handoff.ExternalCaseId, handoff.LastExternalRevision, acceptedAt);
            var snapshot = await adapter.GetStatusAsync(query, ct);
            if (snapshot is null)
            {
                return;
            }

            await ingest.ExecuteAsync(
                @case.Id, handoff.Id, snapshot.ExternalRevision, snapshot.Stage, snapshot.AssignedSpecialist, snapshot.Terminal, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An adapter timeout/5xx is a failed poll attempt, never a status change
            // (support-adapter-v0.md §6.1) — log and let the next tick retry.
            logger.LogWarning(ex, "handoff-status-sync poll failed for handoff {HandoffId}", handoff.Id);
        }
    }
}
