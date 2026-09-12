using TenderHack.Domain.Moderation;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class ProfanityMatcherTests
{
    [Theory]
    [InlineData("это просто сука какая-то ситуация")]
    [InlineData("СУКА, опять не работает")]
    [InlineData("с.у.к.а")] // punctuation-obfuscated
    public void ConfirmedTermIsNotAmbiguous(string text)
    {
        var match = ProfanityMatcher.Evaluate(text);

        Assert.NotNull(match);
        Assert.False(match!.RequiresContextCheck);
    }

    [Fact]
    public void AmbiguousTermRequiresContextCheck()
    {
        var match = ProfanityMatcher.Evaluate("это тупой баг в форме");

        Assert.NotNull(match);
        Assert.True(match!.RequiresContextCheck);
    }

    [Fact]
    public void CleanTextDoesNotMatch()
    {
        var match = ProfanityMatcher.Evaluate("как подать заявку на портале?");

        Assert.Null(match);
    }

    [Fact]
    public void MatchCarriesRuleIdAndVersionForTraceability()
    {
        var match = ProfanityMatcher.Evaluate("да ты хуйня какая-то");

        Assert.NotNull(match);
        Assert.Equal(ProfanityRuleSet.Version, match!.RuleVersion);
        Assert.NotEmpty(match.RuleId);
    }
}
