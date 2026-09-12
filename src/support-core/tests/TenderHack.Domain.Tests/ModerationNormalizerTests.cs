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
}
