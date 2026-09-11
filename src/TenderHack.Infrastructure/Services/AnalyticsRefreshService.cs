using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenderHack.Application.Abstractions;
using TenderHack.Domain.Enums;
using TenderHack.Infrastructure.Persistence;

namespace TenderHack.Infrastructure.Services;

// Runs every minute: numeric metrics computed in SQL, negative feedback sent to ml for
// clustering, result cached. The dashboard only ever reads the cache — see section 7.3.
public sealed class AnalyticsRefreshService(
    IServiceScopeFactory scopeFactory,
    ILogger<AnalyticsRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Analytics refresh cycle failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ml = scope.ServiceProvider.GetRequiredService<IMlService>();
        var cache = scope.ServiceProvider.GetRequiredService<IAnalyticsCache>();

        var resolved = db.Tickets.Where(t => t.Status == TicketStatus.Resolved);

        var totalWithFeedback = await resolved.CountAsync(t => t.Feedback != null, ct);
        var positive = await resolved.CountAsync(t => t.Feedback != null && t.Feedback!.IsPositive, ct);
        var positiveShare = totalWithFeedback == 0 ? 0 : (double)positive / totalWithFeedback;

        var totalTickets = await db.Tickets.CountAsync(ct);
        var escalated = await db.Tickets.CountAsync(
            t => t.Status == TicketStatus.AwaitingOperator || t.Status == TicketStatus.InProgress, ct);
        var escalationShare = totalTickets == 0 ? 0 : (double)escalated / totalTickets;

        var avgResolution = await resolved
            .Select(t => t.UpdatedAt - t.CreatedAt)
            .ToListAsync(ct);
        var averageResolutionTime = avgResolution.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromTicks((long)avgResolution.Average(ts => ts.Ticks));

        var negativeComments = await resolved
            .Where(t => t.Feedback != null && !t.Feedback!.IsPositive && t.Feedback.Comment != null)
            .Select(t => new { t.Id, t.Feedback!.Comment })
            .ToListAsync(ct);

        var clusters = negativeComments.Count == 0
            ? []
            : await ml.ClusterFeedbackAsync(
                negativeComments.Select(c => new FeedbackText(c.Id, c.Comment!)).ToList(), ct);

        var snapshot = new AnalyticsSnapshot(
            DateTimeOffset.UtcNow,
            positiveShare,
            averageResolutionTime,
            escalationShare,
            RepeatContactShare: 0,
            ProblemClusters: clusters);

        await cache.SetAsync(snapshot, ct);
    }
}
