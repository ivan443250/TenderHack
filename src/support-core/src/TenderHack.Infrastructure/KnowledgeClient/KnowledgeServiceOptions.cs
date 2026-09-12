namespace TenderHack.Infrastructure.KnowledgeClient;

/// <summary>Server configuration for the `knowledge` HTTP client (architecture.md §7: per-stage timeouts).</summary>
public sealed class KnowledgeServiceOptions
{
    public const string SectionName = "Knowledge";

    public string BaseAddress { get; set; } = "http://knowledge:8000";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
