using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TenderHack.Api.Tests;

/// <summary>
/// E1 (docs/plans/active/2026-09-demo-readiness.md): HTTP coverage for `DELETE /api/v0/cases/{id}`
/// against the real pipeline — owner scoping, idempotent repeat, live-handoff conflict, and that
/// hiding never makes the case itself unreadable (soft-hide, not delete).
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class HideCaseTests(ApiTestFixture fixture)
{
    private static async Task<string> CreateCaseAsync(HttpClient client)
    {
        var created = await client.PostAsync("/api/v0/cases", content: null);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString()!;
    }

    [Fact]
    public async Task HidingACaseRemovesItFromBothListsButItRemainsReadableDirectly()
    {
        var client = fixture.CreateClient();
        var caseId = await CreateCaseAsync(client);

        var delete = await client.DeleteAsync($"/api/v0/cases/{caseId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var active = await client.GetFromJsonAsync<JsonElement>("/api/v0/cases?status=active");
        Assert.DoesNotContain(active.EnumerateArray(), c => c.GetProperty("case_id").GetString() == caseId);
        var archived = await client.GetFromJsonAsync<JsonElement>("/api/v0/cases?status=archived");
        Assert.DoesNotContain(archived.EnumerateArray(), c => c.GetProperty("case_id").GetString() == caseId);

        var direct = await client.GetAsync($"/api/v0/cases/{caseId}");
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
    }

    [Fact]
    public async Task HidingSomeoneElsesCaseReturnsNotFound()
    {
        var owner = fixture.CreateClient();
        var caseId = await CreateCaseAsync(owner);

        var stranger = fixture.CreateClient();
        await stranger.PostAsync("/api/v0/session", content: null);

        var response = await stranger.DeleteAsync($"/api/v0/cases/{caseId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HidingTwiceIsIdempotent()
    {
        var client = fixture.CreateClient();
        var caseId = await CreateCaseAsync(client);

        var first = await client.DeleteAsync($"/api/v0/cases/{caseId}");
        var second = await client.DeleteAsync($"/api/v0/cases/{caseId}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public async Task HidingACaseWithALiveHandoffReturnsConflict()
    {
        var client = fixture.CreateClient();
        var caseId = await CreateCaseAsync(client);
        var prepare = await client.PostAsync($"/api/v0/cases/{caseId}/handoff/prepare", content: null);
        prepare.EnsureSuccessStatusCode();
        var confirm = await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/handoff/confirm", new { summary = "Резюме" });
        confirm.EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/api/v0/cases/{caseId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("HANDOFF_IN_PROGRESS", body.GetProperty("code").GetString());
    }
}
