using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderHack.Application.Handoff;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;
using TenderHack.Infrastructure.Handoff;

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
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
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
        var submissionPublisher = scope.ServiceProvider.GetRequiredService<HandoffSubmissionPublisher>();
        var events = scope.ServiceProvider.GetRequiredService<ICaseEventReader>();
        var maxAttempts = scope.ServiceProvider.GetRequiredService<IOptions<SupportOptions>>().Value.Submit.MaxAttempts;

        var pending = await outboxReader.ListPendingAsync(HandoffOutboxMessages.Submit, batchSize: 10, ct);

        foreach (var entry in pending)
        {
            await ProcessEntryAsync(entry, cases, events, outboxReader, unitOfWork, adapter, submissionPublisher, clock, maxAttempts, ct);
        }
    }

    private async Task ProcessEntryAsync(
        OutboxEntry entry, ICaseRepository cases, ICaseEventReader events, IOutboxReader outboxReader, IUnitOfWork unitOfWork,
        IHandoffAdapter adapter, HandoffSubmissionPublisher submissionPublisher, TimeProvider clock, int maxAttempts, CancellationToken ct)
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
            // Everything beyond the id is read straight off the loaded aggregate — the package was
            // assembled once at `prepare` time from persisted case state alone (support-adapter-v0.md
            // §2), and `ConfirmedSummary` is the user's own final edited text (set by confirm/retry).
            // A handoff persisted before `package_json` existed has no stored package; rebuild it from
            // the same persisted history `prepare` would have used instead of failing the submission.
            var package = handoff.Package ?? HandoffPackageBuilder.Build(@case, await events.ListAsync(caseId, after: 0, ct));
            var request = new HandoffRequest(
                caseId,
                handoffId,
                handoff.ConfirmedSummary ?? package.DraftSummary,
                package.Channel,
                package.DispatchQueue,
                package.HandoffReason,
                package.UserReportedContext,
                package.VerifiedPortalContext,
                package.AlreadyTried,
                package.UnknownFields,
                package.SourcesChecked,
                package.RelevantMessageIds,
                package.EngineeringReviewSuggested);
            var ack = await adapter.SubmitAsync(request, idempotencyKey: handoffId.ToString(), ct);
            var now = clock.GetUtcNow();

            if (ack.Accepted)
            {
                @case.AcknowledgeHandoff(ack.Simulated, ack.ExternalCaseId, now);
            }
            else
            {
                @case.FailHandoff();
            }

            // Publish the timeline event/notification for whichever outcome just landed — without
            // this the browser sits on `PENDING` until the first polled stage arrives (or forever,
            // for a rejected/failed submission).
            await submissionPublisher.PublishAsync(@case, ack.SafeMessage, now, ct);

            await outboxReader.MarkDeliveredAsync(entry.Id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // support-adapter-v0.md §7 "retries use bounded backoff": a transport failure (unlike an
            // explicit adapter rejection, handled above without ever reaching this catch) gets
            // `MaxAttempts` total tries across outbox redelivery ticks before the handoff is
            // actually marked `FAILED` — `entry.AttemptCount` is this row's PRIOR attempt count, so
            // the attempt that just failed is number `AttemptCount + 1`.
            if (entry.AttemptCount + 1 < maxAttempts)
            {
                await outboxReader.RecordAttemptFailureAsync(entry.Id, ex.Message, ct);
            }
            else
            {
                @case.FailHandoff();
                await submissionPublisher.PublishAsync(@case, safeMessage: null, clock.GetUtcNow(), ct);
                await outboxReader.MarkFailedAsync(entry.Id, ex.Message, ct);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
