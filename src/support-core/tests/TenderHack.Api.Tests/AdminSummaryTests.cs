using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TenderHack.Application.Knowledge;
using Xunit;

namespace TenderHack.Api.Tests;

/// <summary>
/// `/admin/api/summary` against the real pipeline: a turn that actually ran through the orchestrator
/// (fake `knowledge`, real Postgres) must show up in the window with its decision and question text,
/// and must not show up outside the window. The panel page itself is a static embedded resource.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class AdminSummaryTests(ApiTestFixture fixture)
{
    private static async Task<string> CreateCaseAsync(HttpClient client)
    {
        var created = await client.PostAsync("/api/v0/cases", content: null);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString()!;
    }

    private static string Iso(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));

    [Fact]
    public async Task SummaryCountsTheTurnAndListsTheQuestionInsideTheWindow()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
        var client = fixture.CreateClient();
        var caseId = await CreateCaseAsync(client);
        var question = $"admin summary probe {Guid.NewGuid():N}";
        var sent = await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text = question });
        sent.EnsureSuccessStatusCode();
        Assert.Equal("ANSWER", (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decision").GetString());

        var now = DateTimeOffset.UtcNow;
        var response = await client.GetAsync($"/admin/api/summary?from={Iso(now.AddMinutes(-10))}&to={Iso(now.AddMinutes(10))}&limit=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(summary.GetProperty("totals").GetProperty("turns").GetInt32() >= 1);
        Assert.True(summary.GetProperty("decisions").GetProperty("ANSWER").GetInt32() >= 1);
        var row = Assert.Single(summary.GetProperty("questions").EnumerateArray(), q => q.GetProperty("text").GetString() == question);
        Assert.Equal(caseId, row.GetProperty("case_id").GetString());
        Assert.Equal("ANSWER", row.GetProperty("decision").GetString());
        Assert.Contains(summary.GetProperty("top_questions").EnumerateArray(), q => q.GetProperty("text").GetString() == question);
        Assert.Contains(summary.GetProperty("by_day").EnumerateArray(), d => d.GetProperty("answers").GetInt32() >= 1);
    }

    [Fact]
    public async Task SummaryOutsideTheWindowIsEmptyForThatQuestion()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
        var client = fixture.CreateClient();
        var caseId = await CreateCaseAsync(client);
        var question = $"admin summary outside {Guid.NewGuid():N}";
        (await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text = question })).EnsureSuccessStatusCode();

        var past = DateTimeOffset.UtcNow.AddDays(-30);
        var summary = await client.GetFromJsonAsync<JsonElement>($"/admin/api/summary?from={Iso(past.AddDays(-1))}&to={Iso(past)}");

        Assert.DoesNotContain(summary.GetProperty("questions").EnumerateArray(), q => q.GetProperty("text").GetString() == question);
    }

    [Fact]
    public async Task InvertedWindowIsRejected()
    {
        var client = fixture.CreateClient();
        var now = DateTimeOffset.UtcNow;

        var response = await client.GetAsync($"/admin/api/summary?from={Iso(now)}&to={Iso(now.AddHours(-1))}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task PanelPageIsServedAsHtmlWithoutAnOwnerSession()
    {
        // A fresh client: no POST /api/v0/session, no owner cookie — the panel is not an owner-scoped surface.
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("/admin/api/summary", await response.Content.ReadAsStringAsync());
    }
}
