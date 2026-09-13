using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TenderHack.Api.Tests;

/// <summary>E3 (docs/plans/active/2026-09-demo-readiness.md): HTTP coverage for the "Материалы" tab
/// proxy against the real pipeline — owner scoping and the shape/values `knowledge`'s
/// <see cref="TenderHack.Application.Tests.Fakes.FakeKnowledgeService"/> returns.</summary>
[Collection(ApiTestCollection.Name)]
public sealed class MaterialsEndpointsTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task MaterialsEndpointRequiresAnOwnerSession()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/v0/materials");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MaterialsEndpointListsDocumentsFromKnowledge()
    {
        var client = fixture.CreateClient();
        await client.PostAsync("/api/v0/session", content: null);

        var response = await client.GetAsync("/api/v0/materials");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("snapshot-1", body.GetProperty("snapshot_id").GetString());
        var materials = body.GetProperty("materials").EnumerateArray().ToArray();
        Assert.Single(materials);
        Assert.Equal("doc-1", materials[0].GetProperty("document_id").GetString());
        Assert.Equal("Инструкция.pdf", materials[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task MaterialSectionsEndpointListsSectionsFromKnowledge()
    {
        var client = fixture.CreateClient();
        await client.PostAsync("/api/v0/session", content: null);

        var response = await client.GetAsync("/api/v0/materials/doc-1/sections");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("doc-1", body.GetProperty("document_id").GetString());
        var sections = body.GetProperty("sections").EnumerateArray().ToArray();
        Assert.Single(sections);
        Assert.Equal("frag-1", sections[0].GetProperty("first_fragment_id").GetString());
    }
}
