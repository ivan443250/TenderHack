using TenderHack.Domain.Cases;
using TenderHack.Domain.Moderation;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class ModerationPolicyTests
{
    [Theory]
    [InlineData(0, 1, Decision.ModerationWarning)]
    [InlineData(1, 1, Decision.ModerationClose)]
    [InlineData(0, 2, Decision.ModerationWarning)]
    [InlineData(1, 2, Decision.ModerationWarning)]
    [InlineData(2, 2, Decision.ModerationClose)]
    public void EvaluateAppliesWarningFirstThreshold(int currentWarningCount, int closeAfterWarnings, Decision expected)
    {
        var (decision, _) = ModerationPolicy.Evaluate(currentWarningCount, closeAfterWarnings);

        Assert.Equal(expected, decision);
    }

    [Fact]
    public void EvaluateAlwaysIncrementsTheCounter()
    {
        var (_, newCount) = ModerationPolicy.Evaluate(currentWarningCount: 3, closeAfterWarnings: 1);

        Assert.Equal(4, newCount);
    }
}
