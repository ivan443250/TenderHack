using TenderHack.Domain.Routing;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class SupportScopeDetectorTests
{
    [Theory]
    [InlineData("привет")]
    [InlineData("здравствуйте!")]
    [InlineData("спасибо")]
    [InlineData("как дела?")]
    [InlineData("как приготовить борщ?")]
    [InlineData("как настроить Kafka?")]
    [InlineData("как подключить Kafka к Порталу поставщиков?")]
    public void DetectsOnlyObviousOutOfScopeMessages(string text)
    {
        Assert.True(SupportScopeDetector.TryGetReply(text, out var reply));
        Assert.NotEmpty(reply);
    }

    [Theory]
    [InlineData("как создать СТЕ для оферты?")]
    [InlineData("как загрузиь YML в католог?")]
    [InlineData("как загрузить файл?")]
    [InlineData("как настроить интеграцию?")]
    [InlineData("как добавить МЧД?")]
    [InlineData("что делать с ошибкой УПД?")]
    public void KeepsPortalQuestionsInKnowledgePipeline(string text)
    {
        Assert.False(SupportScopeDetector.TryGetReply(text, out _));
    }

    [Fact]
    public void UsesDedicatedGreetingAndThanksReplies()
    {
        Assert.True(SupportScopeDetector.TryGetReply("привет", out var greeting));
        Assert.Contains("Портала поставщиков", greeting);
        Assert.True(SupportScopeDetector.TryGetReply("спасибо", out var thanks));
        Assert.Contains("Пожалуйста", thanks);
    }
}
