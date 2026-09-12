using TenderHack.Domain.Moderation;
using Xunit;

namespace TenderHack.Domain.Tests;

/// <summary>
/// quality.md §6 minimum coverage: direct profanity, obfuscation, separators/repeated characters
/// and Latin look-alikes are already exercised in <see cref="ProfanityMatcherTests"/> /
/// <see cref="ModerationNormalizerTests"/>. This file adds the two categories not covered
/// elsewhere — a benign word must never match by mere substring, and ordinary anger/criticism
/// without profanity must pass through untouched — as one regression fixture, so a future rule
/// change that widens a pattern too far fails loudly here instead of silently in production.
/// </summary>
public sealed class ModerationRegressionTests
{
    [Theory]
    // "сукно" (fabric) contains "сук" as a substring but is not a form of "сука".
    [InlineData("нужна ткань, сукно для формы")]
    // "сукиных" would only be a false positive if the rule dropped its trailing-letter guard.
    [InlineData("сукно и другие материалы на складе")]
    // "тупик" (dead end) contains "туп" but is not "тупой".
    [InlineData("документ попал в тупик согласования")]
    // "дурман" / "дуршлаг"-style words contain "дур" but are not "дура".
    [InlineData("в разделе дурмана ошибок нет")]
    public void BenignWordsContainingAProfaneSubstringDoNotMatch(string text)
    {
        var match = ProfanityMatcher.Evaluate(text);

        Assert.Null(match);
    }

    [Theory]
    // Ordinary frustration/criticism without profanity — must never trigger a moderation rule hit,
    // confirmed or ambiguous, however sharply worded.
    [InlineData("это ужасно, всё сломано, я очень зол")]
    [InlineData("невозможно работать с этим порталом, кошмар какой-то")]
    [InlineData("вы издеваетесь? третий день ничего не работает")]
    [InlineData("отвратительная поддержка, никто не отвечает")]
    public void OrdinaryAngerOrCriticismWithoutProfanityNeverMatches(string text)
    {
        var match = ProfanityMatcher.Evaluate(text);

        Assert.Null(match);
    }

    [Fact]
    public void UncertainAmbiguousMatchDoesNotCountAsConfirmedUnderTheWarningFirstPolicy()
    {
        // product-spec.md §14 step 4: UNCERTAIN from knowledge.moderation_context must not increment
        // the warning counter — the ambiguous-list hit itself is only a candidate for that check.
        var match = ProfanityMatcher.Evaluate("это тупой баг в форме");

        Assert.NotNull(match);
        Assert.True(match!.RequiresContextCheck);
    }
}
