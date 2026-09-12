using TenderHack.Domain.Handoffs;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class DemoHandoffScriptTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);

    [Fact]
    public void BeforeFirstDelayNothingIsVisibleYet()
    {
        Assert.Null(DemoHandoffScript.NextStep(TimeSpan.FromSeconds(5), Delay, lastAppliedRevision: -1));
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(20, 2)]
    [InlineData(30, 3)]
    [InlineData(40, 4)]
    [InlineData(90, 4)] // caps at revision 4, does not keep advancing
    public void RevisionDueAtFollowsElapsedTime(int elapsedSeconds, long expectedRevision)
    {
        Assert.Equal(expectedRevision, DemoHandoffScript.RevisionDueAt(TimeSpan.FromSeconds(elapsedSeconds), Delay));
    }

    [Theory]
    [InlineData(1, "QUEUED")]
    [InlineData(2, "ASSIGNED")]
    [InlineData(3, "IN_PROGRESS")]
    [InlineData(4, "RESOLVED")]
    public void EachRevisionHasItsScriptedStage(long revision, string expectedStageCode)
    {
        var step = DemoHandoffScript.StepAt(revision);

        Assert.NotNull(step);
        Assert.Equal(revision, step!.ExternalRevision);
        Assert.Equal(expectedStageCode, step.Stage.Code);
    }

    [Fact]
    public void StepsAreNeverSkippedWhenPollingIsSlowerThanTheStageDelay()
    {
        // Regression: the adapter used to return "whatever step is latest by now", so a 15 s poll
        // against a 10 s stage delay produced QUEUED → IN_PROGRESS → RESOLVED and the specialist
        // (revision 2) never reached the UI. Each poll must hand out exactly the next revision.
        var elapsed = TimeSpan.FromSeconds(45); // all four steps are already due
        var applied = -1L; // Handoff.LastExternalRevision before any status fact
        var seen = new List<long>();

        while (DemoHandoffScript.NextStep(elapsed, Delay, applied) is { } step)
        {
            seen.Add(step.ExternalRevision);
            applied = step.ExternalRevision;
        }

        Assert.Equal([1L, 2L, 3L, 4L], seen);
    }

    [Fact]
    public void NextStepWaitsUntilTheNextRevisionIsDue()
    {
        Assert.Null(DemoHandoffScript.NextStep(TimeSpan.FromSeconds(15), Delay, lastAppliedRevision: 1));
        Assert.Equal(2, DemoHandoffScript.NextStep(TimeSpan.FromSeconds(20), Delay, lastAppliedRevision: 1)!.ExternalRevision);
    }

    [Fact]
    public void OnlyRevision2CarriesTheDemoSpecialist()
    {
        Assert.Null(DemoHandoffScript.StepAt(1)!.AssignedSpecialist);
        Assert.Equal("demo-1", DemoHandoffScript.StepAt(2)!.AssignedSpecialist!.Ref);
        Assert.Null(DemoHandoffScript.StepAt(3)!.AssignedSpecialist);
    }

    [Fact]
    public void OnlyRevision4IsTerminal()
    {
        Assert.Null(DemoHandoffScript.StepAt(3)!.Terminal);
        Assert.Equal(HandoffTerminalOutcome.Resolved, DemoHandoffScript.StepAt(4)!.Terminal);
    }

    [Fact]
    public void SameInputsAlwaysProduceTheSameStepDeterministically()
    {
        var first = DemoHandoffScript.NextStep(TimeSpan.FromSeconds(23), Delay, 1);
        var second = DemoHandoffScript.NextStep(TimeSpan.FromSeconds(23), Delay, 1);

        Assert.Equal(first, second);
    }
}
