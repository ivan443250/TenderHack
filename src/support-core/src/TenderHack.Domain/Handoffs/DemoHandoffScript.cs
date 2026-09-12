namespace TenderHack.Domain.Handoffs;

/// <summary>
/// Deterministic demo status timeline (support-adapter-v0.md §8): a pure function of elapsed time
/// since acceptance and of the last revision the caller already applied, so the same handoff walks
/// the same script step by step on every run — reproducible for demo rehearsals and E2E.
/// </summary>
public static class DemoHandoffScript
{
    public const long LastRevision = 4;

    /// <summary>
    /// The next unreported step, or null when nothing new is due yet. Steps are never skipped: a
    /// poll interval longer than <paramref name="stageDelay"/> still delivers revisions 1, 2, 3, 4
    /// one per poll — otherwise the only step that carries the specialist (revision 2) could be lost
    /// depending on poll phase.
    /// </summary>
    public static DemoHandoffStep? NextStep(TimeSpan elapsedSinceAccepted, TimeSpan stageDelay, long lastAppliedRevision)
    {
        var next = Math.Max(lastAppliedRevision, 0) + 1;
        return next <= RevisionDueAt(elapsedSinceAccepted, stageDelay) ? StepAt(next) : null;
    }

    /// <summary>Highest revision the script has reached after <paramref name="elapsedSinceAccepted"/>; 0 before the first stage delay.</summary>
    public static long RevisionDueAt(TimeSpan elapsedSinceAccepted, TimeSpan stageDelay)
    {
        if (stageDelay <= TimeSpan.Zero)
        {
            stageDelay = TimeSpan.FromSeconds(10);
        }

        return Math.Min(Math.Max(elapsedSinceAccepted.Ticks / stageDelay.Ticks, 0), LastRevision);
    }

    public static DemoHandoffStep? StepAt(long revision) => revision switch
    {
        1 => new DemoHandoffStep(1, new HandoffStage("QUEUED", "В очереди"), null, null),
        2 => new DemoHandoffStep(2, new HandoffStage("ASSIGNED", "Назначен специалист"), new AssignedSpecialist("demo-1", "Демо-специалист"), null),
        3 => new DemoHandoffStep(3, new HandoffStage("IN_PROGRESS", "В работе"), null, null),
        4 => new DemoHandoffStep(4, new HandoffStage("RESOLVED", "Решено"), null, HandoffTerminalOutcome.Resolved),
        _ => null,
    };
}

public sealed record DemoHandoffStep(long ExternalRevision, HandoffStage Stage, AssignedSpecialist? AssignedSpecialist, HandoffTerminalOutcome? Terminal);
