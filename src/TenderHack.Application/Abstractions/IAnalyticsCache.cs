namespace TenderHack.Application.Abstractions;

public interface IAnalyticsCache
{
    Task<AnalyticsSnapshot?> GetAsync(CancellationToken ct);

    Task SetAsync(AnalyticsSnapshot snapshot, CancellationToken ct);
}

// Populated by AnalyticsRefreshService every minute — see section 7.3.
public sealed record AnalyticsSnapshot(
    DateTimeOffset ComputedAt,
    double PositiveFeedbackShare,
    TimeSpan AverageResolutionTime,
    double EscalationShare,
    double RepeatContactShare,
    IReadOnlyList<ProblemCluster> ProblemClusters);
