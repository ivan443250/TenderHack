using TenderHack.Application.Abstractions;

namespace TenderHack.Application.Analytics;

// Dashboard reads whatever AnalyticsRefreshService last computed; never waits on ml — see section 7.3.
public sealed class GetAnalyticsHandler(IAnalyticsCache cache)
{
    public Task<AnalyticsSnapshot?> HandleAsync(CancellationToken ct) => cache.GetAsync(ct);
}
