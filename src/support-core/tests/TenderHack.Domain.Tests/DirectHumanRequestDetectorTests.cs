using TenderHack.Domain.Routing;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class DirectHumanRequestDetectorTests
{
    [Theory]
    [InlineData("хочу поговорить с оператором")]
    [InlineData("соедините меня с человеком")]
    [InlineData("нужна техподдержка")]
    [InlineData("ПОЗОВИТЕ СПЕЦИАЛИСТА")]
    public void DetectsExplicitRequests(string text)
    {
        Assert.True(DirectHumanRequestDetector.IsExplicitRequest(text));
    }

    [Theory]
    [InlineData("как подать заявку на портале?")]
    [InlineData("почему не грузится документ")]
    public void DoesNotFlagOrdinaryQuestions(string text)
    {
        Assert.False(DirectHumanRequestDetector.IsExplicitRequest(text));
    }
}
