using TenderHack.Domain.Handoffs;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class DemoHandoffScriptTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);

    [Fact]
    public void BeforeFirstDelayNothingIsVisibleYet()
    {
        var step = DemoHandoffScript.StepFor(TimeSpan.FromSeconds(5), Delay);

        Assert.Null(step);
    }

    [Theory]
    [InlineData(10, 1, "QUEUED")]
    [InlineData(20, 2, "ASSIGNED")]
    [InlineData(30, 3, "IN_PROGRESS")]
    [InlineData(40, 4, "RESOLVED")]
    [InlineData(90, 4, "RESOLVED")] // caps at revision 4, does not keep advancing
    public void ProducesTheExactScriptedRevision(int elapsedSeconds, long expectedRevision, string expectedStageCode)
    {
        var step = DemoHandoffScript.StepFor(TimeSpan.FromSeconds(elapsedSeconds), Delay);

        Assert.NotNull(step);
        Assert.Equal(expectedRevision, step!.ExternalRevision);
        Assert.Equal(expectedStageCode, step.Stage.Code);
    }

    [Fact]
    public void OnlyRevision2CarriesTheDemoSpecialist()
    {
        Assert.Null(DemoHandoffScript.StepFor(TimeSpan.FromSeconds(10), Delay)!.AssignedSpecialist);
        var revision2 = DemoHandoffScript.StepFor(TimeSpan.FromSeconds(20), Delay)!;
        Assert.Equal("demo-1", revision2.AssignedSpecialist!.Ref);
        Assert.Null(DemoHandoffScript.StepFor(TimeSpan.FromSeconds(30), Delay)!.AssignedSpecialist);
    }

    [Fact]
    public void OnlyRevision4IsTerminal()
    {
        Assert.Null(DemoHandoffScript.StepFor(TimeSpan.FromSeconds(30), Delay)!.Terminal);
        Assert.Equal(HandoffTerminalOutcome.Resolved, DemoHandoffScript.StepFor(TimeSpan.FromSeconds(40), Delay)!.Terminal);
    }

    [Fact]
    public void SameElapsedTimeAlwaysProducesTheSameStepDeterministically()
    {
        var first = DemoHandoffScript.StepFor(TimeSpan.FromSeconds(23), Delay);
        var second = DemoHandoffScript.StepFor(TimeSpan.FromSeconds(23), Delay);

        Assert.Equal(first, second);
    }
}
