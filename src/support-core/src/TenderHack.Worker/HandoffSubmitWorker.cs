using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenderHack.Application.Handoff;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Worker;

/// <summary>
/// Delivers `handoff.submit` outbox rows to <see cref="IHandoffAdapter.SubmitAsync"/> — at-least-once,
/// idempotent by handoff id (architecture.md §12, §15).
/// </summary>
public sealed class HandoffSubmitWorker(IServiceScopeFactory scopeFactory, ILogger<HandoffSubmitWorker> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "handoff-submit tick failed");
            }

            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var outboxReader = scope.ServiceProvider.GetRequiredService<IOutboxReader>();
        var cases = scope.ServiceProvider.GetRequiredService<ICaseRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var adapter = scope.ServiceProvider.GetRequiredService<IHandoffAdapter>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var pending = await outboxReader.ListPendingAsync(HandoffOutboxMessages.Submit, batchSize: 10, ct);

        foreach (var entry in pending)
        {
            await ProcessEntryAsync(entry, cases, outboxReader, unitOfWork, adapter, clock, ct);
        }
    }

    private async Task ProcessEntryAsync(
        OutboxEntry entry, ICaseRepository cases, IOutboxReader outboxReader, IUnitOfWork unitOfWork,
        IHandoffAdapter adapter, TimeProvider clock, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<HandoffSubmitPayload>(entry.PayloadJson)
            ?? throw new InvalidOperationException($"Outbox row {entry.Id} has no payload.");
        var caseId = new CaseId(Guid.Parse(payload.CaseId));
        var handoffId = new HandoffId(Guid.Parse(payload.HandoffId));

        var @case = await cases.FindAsync(caseId, ct);
        if (@case?.Handoff is not { } handoff || handoff.Id != handoffId || handoff.Status != HandoffStatus.Pending)
        {
            // Nothing left to do: already acknowledged/failed by a previous attempt (at-least-once
            // redelivery after a crash between the domain commit and this mark), or state moved on.
            await outboxReader.MarkDeliveredAsync(entry.Id, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        try
        {
            var request = new HandoffRequest(caseId, handoffId, payload.Summary, payload.DispatchQueue, payload.ReasonCodes, payload.EngineeringReviewSuggested);
            var ack = await adapter.SubmitAsync(request, idempotencyKey: handoffId.ToString(), ct);

            if (ack.Accepted)
            {
                @case.AcknowledgeHandoff(ack.Simulated, ack.ExternalCaseId, clock.GetUtcNow());
            }
            else
            {
                @case.FailHandoff();
            }

            await outboxReader.MarkDeliveredAsync(entry.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            @case.FailHandoff();
            await outboxReader.MarkFailedAsync(entry.Id, ex.Message, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
