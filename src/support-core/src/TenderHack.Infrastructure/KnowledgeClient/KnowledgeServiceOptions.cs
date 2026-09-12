namespace TenderHack.Infrastructure.KnowledgeClient;

/// <summary>Server configuration for the `knowledge` HTTP client (architecture.md §7: per-stage timeouts).</summary>
public sealed class KnowledgeServiceOptions
{
    public const string SectionName = "Knowledge";

    public string BaseUrl { get; set; } = "http://knowledge:8000";

    /// <summary>
    /// Outer `HttpClient.Timeout` safety net — must stay at or above the largest value in
    /// <see cref="Timeouts"/>, since every per-call timeout below is enforced by a linked
    /// `CancellationTokenSource`, not by this value. It only guards against a per-stage timeout
    /// that was misconfigured larger than this ceiling.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);

    public KnowledgeStageTimeouts Timeouts { get; set; } = new();
}

/// <summary>
/// Per-stage timeouts (architecture.md §10: "per-stage timeouts are `api` config"). A single global
/// timeout does not fit: `draft`/`verify` run an actual local generation model and can legitimately
/// take far longer than a lexical `retrieve` or `understand` call.
/// </summary>
public sealed class KnowledgeStageTimeouts
{
    public TimeSpan Understand { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan ModerationContext { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan Retrieve { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan Answerability { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Generation is the slowest stage on real local inference hardware — kept generous by default.</summary>
    public TimeSpan Draft { get; set; } = TimeSpan.FromSeconds(90);

    public TimeSpan Verify { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan Source { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Non-interactive calls (quality pushes, analytics reads) that are not on the user-facing turn latency budget.</summary>
    public TimeSpan Background { get; set; } = TimeSpan.FromSeconds(30);
}
