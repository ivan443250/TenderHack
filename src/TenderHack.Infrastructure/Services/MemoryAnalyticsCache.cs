using Microsoft.Extensions.Caching.Memory;
using TenderHack.Application.Abstractions;

namespace TenderHack.Infrastructure.Services;

public sealed class MemoryAnalyticsCache(IMemoryCache cache) : IAnalyticsCache
{
    private const string CacheKey = "analytics:snapshot";

    public Task<AnalyticsSnapshot?> GetAsync(CancellationToken ct) =>
        Task.FromResult(cache.Get<AnalyticsSnapshot>(CacheKey));

    public Task SetAsync(AnalyticsSnapshot snapshot, CancellationToken ct)
    {
        cache.Set(CacheKey, snapshot);
        return Task.CompletedTask;
    }
}
