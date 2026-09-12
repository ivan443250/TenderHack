using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenderHack.Application.Knowledge;
using TenderHack.Application.Ports;

namespace TenderHack.Worker;

/// <summary>
/// Delivers `quality.turn`/`quality.feedback`/`quality.completion` outbox rows to `knowledge`
/// (knowledge-v0.md §10) — at-least-once, idempotent on the receiving side by
/// (turn_id, revision) / feedback_id / case_id respectively. Unlike handoff submission, a failed
/// push has no user-triggered retry, so it stays pending and is redelivered next tick.
/// </summary>
public sealed class QualityPushWorker(IServiceScopeFactory scopeFactory, ILogger<QualityPushWorker> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Filter on *our* token, not on the exception type: an HttpClient timeout surfaces as
                // a TaskCanceledException, and an `is not OperationCanceledException` filter let one
                // escape ExecuteAsync — which, under the host's default StopHost policy, took the
                // whole api-worker process down with it. Only a real shutdown may leave this loop.
                logger.LogError(ex, "quality-push tick failed");
            }

            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var outboxReader = scope.ServiceProvider.GetRequiredService<IOutboxReader>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var knowledge = scope.ServiceProvider.GetRequiredService<IKnowledgeService>();

        await ProcessMessageTypeAsync(QualityOutboxMessages.Turn, outboxReader, unitOfWork,
            async (json, innerCt) => await knowledge.PushQualityTurnAsync(Deserialize<QualityTurnPush>(json), innerCt), ct);

        await ProcessMessageTypeAsync(QualityOutboxMessages.Feedback, outboxReader, unitOfWork,
            async (json, innerCt) => await knowledge.PushQualityFeedbackAsync(Deserialize<QualityFeedbackPush>(json), innerCt), ct);

        await ProcessMessageTypeAsync(QualityOutboxMessages.Completion, outboxReader, unitOfWork,
            async (json, innerCt) => await knowledge.PushQualityCompletionAsync(Deserialize<QualityCompletionPush>(json), innerCt), ct);
    }

    private async Task ProcessMessageTypeAsync(
        string messageType, IOutboxReader outboxReader, IUnitOfWork unitOfWork, Func<string, CancellationToken, Task> push, CancellationToken ct)
    {
        var pending = await outboxReader.ListPendingAsync(messageType, batchSize: 20, ct);

        foreach (var entry in pending)
        {
            try
            {
                await push(entry.PayloadJson, ct);
                await outboxReader.MarkDeliveredAsync(entry.Id, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "quality push {MessageType} (outbox row {Id}) failed, will retry", messageType, entry.Id);
                await outboxReader.RecordAttemptFailureAsync(entry.Id, ex.Message, ct);
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException($"Outbox payload could not be deserialized as {typeof(T).Name}.");
}
