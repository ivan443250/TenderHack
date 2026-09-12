namespace TenderHack.Worker;

public sealed class StaleTurnCleanupOptions
{
    public const string SectionName = "StaleTurn";

    /// <summary>A turn stuck `QUEUED`/`RUNNING` longer than this is treated as an orphaned crash artifact.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);
}
