namespace TenderHack.Domain.Handoffs;

/// <summary>
/// Deterministic demo status timeline (support-adapter-v0.md §8): a pure function of elapsed time
/// since acceptance, so the same handoff produces the same script on every run — reproducible for
/// demo rehearsals and E2E.
/// </summary>
public static class DemoHandoffScript
{
    /// <summary>Null before the first step becomes visible (elapsed &lt; one stage delay).</summary>
    public static DemoHandoffStep? StepFor(TimeSpan elapsedSinceAccepted, TimeSpan stageDelay)
    {
        if (stageDelay <= TimeSpan.Zero)
        {
            stageDelay = TimeSpan.FromSeconds(10);
        }

        var revision = Math.Min((long)(elapsedSinceAccepted.Ticks / stageDelay.Ticks), 4);

        return revision switch
        {
            0 => null,
            1 => new DemoHandoffStep(1, new HandoffStage("QUEUED", "В очереди"), null, null),
            2 => new DemoHandoffStep(2, new HandoffStage("ASSIGNED", "Назначен специалист"), new AssignedSpecialist("demo-1", "Демо-специалист"), null),
            3 => new DemoHandoffStep(3, new HandoffStage("IN_PROGRESS", "В работе"), null, null),
            _ => new DemoHandoffStep(4, new HandoffStage("RESOLVED", "Решено"), null, HandoffTerminalOutcome.Resolved),
        };
    }
}

public sealed record DemoHandoffStep(long ExternalRevision, HandoffStage Stage, AssignedSpecialist? AssignedSpecialist, HandoffTerminalOutcome? Terminal);
