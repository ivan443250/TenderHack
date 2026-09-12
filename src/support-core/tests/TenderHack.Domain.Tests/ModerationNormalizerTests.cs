using TenderHack.Domain.Moderation;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class ModerationNormalizerTests
{
    [Fact]
    public void LowercasesText()
    {
        Assert.Equal("привет", ModerationNormalizer.Normalize("ПрИвЕт"));
    }

    [Fact]
    public void StripsPunctuationBetweenLetters()
    {
        Assert.Equal("сука", ModerationNormalizer.Normalize("с.у.к.а"));
    }

    [Fact]
    public void CollapsesStretchedLetters()
    {
        Assert.Equal("сука", ModerationNormalizer.Normalize("сууукаааа"));
    }

    [Fact]
    public void PreservesWordBoundaries()
    {
        Assert.Equal("привет мир", ModerationNormalizer.Normalize("Привет,  мир!"));
    }

    [Theory]
    [InlineData("сук4", "сука")]
    [InlineData("п1зд3ц", "пиздец")]
    [InlineData("бл@", "бла")]
    [InlineData("$ука", "сука")]
    public void LeetSpeakSubstitutionsProduceCyrillicLetters(string obfuscated, string expected)
    {
        // Regression: '4' used to map to Latin 'a', so "сук4" normalized to "сукa" — a string no
        // Cyrillic-only rule could ever match.
        Assert.Equal(expected, ModerationNormalizer.Normalize(obfuscated));
    }

    [Theory]
    [InlineData("cyka", "сука")]
    [InlineData("сукa", "сука")] // Latin 'a' at the end
    public void LatinLookAlikesAreFoldedIntoCyrillic(string mixed, string expected)
    {
        Assert.Equal(expected, ModerationNormalizer.Normalize(mixed));
    }
}
