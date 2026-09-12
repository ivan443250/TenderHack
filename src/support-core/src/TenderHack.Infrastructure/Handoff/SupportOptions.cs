namespace TenderHack.Infrastructure.Handoff;

/// <summary>Server configuration for the support adapter boundary (support-adapter-v0.md §8, §6.1, §6.2).</summary>
public sealed class SupportOptions
{
    public const string SectionName = "Support";

    /// <summary>Only `demo` is implemented; a real Portal adapter is a future adapter against the same port.</summary>
    public string Adapter { get; set; } = "demo";

    public DemoOptions Demo { get; set; } = new();

    public StatusPollOptions StatusPoll { get; set; } = new();

    public WebhookOptions Webhook { get; set; } = new();

    public SubmitOptions Submit { get; set; } = new();

    public sealed class SubmitOptions
    {
        /// <summary>
        /// support-adapter-v0.md §7 "retries use bounded backoff": a transport failure (timeout,
        /// connection refused) gets this many total attempts across outbox redelivery ticks before
        /// the handoff is marked `FAILED` and the user has to explicitly retry. An adapter that
        /// actively rejects the submission (`HandoffAck.Accepted = false`) is not retried at all —
        /// that is an explicit answer, not a transient failure.
        /// </summary>
        public int MaxAttempts { get; set; } = 3;
    }

    public sealed class DemoOptions
    {
        public DemoSubmitMode Submit { get; set; } = DemoSubmitMode.Success;

        public DemoStatusMode Status { get; set; } = DemoStatusMode.Staged;

        public int StageDelaySeconds { get; set; } = 10;
    }

    public sealed class StatusPollOptions
    {
        /// <summary>
        /// Poll interval. The frozen contract specifies exponential backoff from `Initial` to `Max`;
        /// this stage implements a fixed interval (this value) as a scoped-down first cut — real
        /// per-handoff backoff scheduling is a follow-up hardening item, not a correctness gap.
        /// </summary>
        public TimeSpan Initial { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>Reserved for the per-handoff backoff ceiling described above; not read by the current fixed-interval poll.</summary>
        public TimeSpan Max { get; set; } = TimeSpan.FromMinutes(5);

        public TimeSpan Ttl { get; set; } = TimeSpan.FromDays(7);
    }

    public sealed class WebhookOptions
    {
        public bool Enabled { get; set; }

        public string? Secret { get; set; }
    }
}

public enum DemoSubmitMode
{
    Success,
    Timeout,
    Failure,
}

public enum DemoStatusMode
{
    None,
    Staged,
}
