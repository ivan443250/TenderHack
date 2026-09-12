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
    [InlineData("дайте живого человека")]
    [InlineData("переключите на специалиста")]
    [InlineData("нужен оператор портала")]
    public void DetectsExplicitRequests(string text)
    {
        Assert.True(DirectHumanRequestDetector.IsExplicitRequest(text));
    }

    [Theory]
    [InlineData("как подать заявку на портале?")]
    [InlineData("почему не грузится документ")]
    // Regression: "оператор ЭДО"/"оператор электронного документооборота" is ordinary domain
    // vocabulary (product-spec.md §21), not a request for a human — it must not force a false
    // HANDOFF_OFFER that skips retrieval.
    [InlineData("какого оператора ЭДО выбрать для подключения?")]
    [InlineData("как сменить оператора электронного документооборота")]
    public void DoesNotFlagOrdinaryQuestions(string text)
    {
        Assert.False(DirectHumanRequestDetector.IsExplicitRequest(text));
    }
}
