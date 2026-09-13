using TenderHack.Api.Contracts;
using TenderHack.Domain.Cases;
using Xunit;

namespace TenderHack.Api.Tests;

public sealed class CaseListTitleTests
{
    [Fact]
    public void MissingFirstMessageUsesFallbackWithoutLeakingCaseId()
    {
        var @case = new Case(CaseId.New(), "owner", DateTimeOffset.UtcNow);

        var item = CaseMapper.ToListItem(@case);

        Assert.Equal("Новый чат", item.Title);
        Assert.DoesNotContain(@case.Id.ToString(), item.Title);
    }

    [Fact]
    public void LongFirstMessageProducesCompactEllipsizedTitle()
    {
        var title = CaseMapper.BuildCaseTitle("Что делать при ошибке отправки документа исполнения и повторной ошибке?");

        Assert.True(title.Length <= 36);
        Assert.EndsWith("…", title);
    }
}
