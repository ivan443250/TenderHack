using System.Diagnostics;
using TenderHack.Application.Knowledge;

namespace TenderHack.Application.Orchestration;

/// <summary>Accumulates the four stage timings knowledge-v0.md §10's `stage_timings` reports — `null` for stages a turn never reached.</summary>
internal sealed class StageTimingsAccumulator
{
    public int? UnderstandMs { get; set; }
    public int? RetrieveMs { get; set; }
    public int? DraftMs { get; set; }
    public int? VerifyMs { get; set; }

    public StageTimings ToPush() => new(UnderstandMs, RetrieveMs, DraftMs, VerifyMs);

    public static async Task<T> TimeAsync<T>(Func<Task<T>> call, Action<int> recordMs)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await call();
        recordMs((int)stopwatch.ElapsedMilliseconds);
        return result;
    }
}
