using System.Net;
using System.Text.Json;
using TenderHack.Infrastructure.KnowledgeClient.Generated;
using Xunit;

namespace TenderHack.Contract.Tests;

public sealed class KnowledgeWireContractTests
{
    [Fact]
    public async Task EntityProvenanceUsesFrozenLowercaseWireValues()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://knowledge.invalid/") };
        var client = new KnowledgeApiClient(httpClient);

        await client.RetrieveAsync(
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            new RetrieveRequest
            {
                Query = "contract-wire-test",
                Entities =
                [
                    new Entity
                    {
                        Type = "document_type",
                        Value = "STE",
                        Provenance = EntityProvenance.Inferred,
                    },
                ],
                Corpus = Corpus.NORMATIVE,
            });

        using var document = JsonDocument.Parse(handler.RequestBody);
        var wireValue = document.RootElement
            .GetProperty("entities")[0]
            .GetProperty("provenance")
            .GetString();

        Assert.Equal("inferred", wireValue);
    }

    [Fact]
    public async Task MaterialsResponseDeserializesNullableFieldsCorrectly()
    {
        // E3 (docs/plans/active/2026-09-demo-readiness.md): a document with no declared_version/
        // declared_date (both nullable on the wire, knowledge-v0.openapi.yaml MaterialSummary) must
        // deserialize as null, not throw or default to empty string.
        const string response = """
            {"snapshot_id":"snap_real","materials":[
              {"document_id":"doc_a","title":"Инструкция.pdf","declared_version":"v11","declared_date":"2025-03-01","page_count":93,"fragment_count":1200},
              {"document_id":"doc_b","title":"Регламент.pdf","declared_version":null,"declared_date":null,"page_count":40,"fragment_count":300}
            ]}
            """;
        using var httpClient = new HttpClient(new FixedResponseHandler(response)) { BaseAddress = new Uri("http://knowledge.invalid/") };
        var client = new KnowledgeApiClient(httpClient);

        var result = await client.ListMaterialsAsync(Guid.NewGuid());

        Assert.Equal("snap_real", result.Snapshot_id);
        Assert.Equal(2, result.Materials.Count);
        Assert.Equal("v11", result.Materials[0].Declared_version);
        Assert.Null(result.Materials[1].Declared_version);
        Assert.Null(result.Materials[1].Declared_date);
    }

    [Fact]
    public async Task MaterialSectionsResponseDeserializesNullSectionCorrectly()
    {
        // A fragment with no `section` (e.g. a cover page) must round-trip as a null `section`, not
        // an empty string or a missing key that trips a required-property check.
        const string response = """
            {"snapshot_id":"snap_real","document_id":"doc_a","sections":[
              {"section":"1. Общие положения","page_start":1,"page_end":5,"first_fragment_id":"frag_1"},
              {"section":null,"page_start":90,"page_end":93,"first_fragment_id":"frag_9"}
            ]}
            """;
        using var httpClient = new HttpClient(new FixedResponseHandler(response)) { BaseAddress = new Uri("http://knowledge.invalid/") };
        var client = new KnowledgeApiClient(httpClient);

        var result = await client.ListMaterialSectionsAsync(Guid.NewGuid(), "doc_a");

        Assert.Equal(2, result.Sections.Count);
        Assert.Null(result.Sections[1].Section);
        Assert.Equal("frag_9", result.Sections[1].First_fragment_id);
    }

    private sealed class FixedResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            const string response = "{\"snapshot_id\":\"snap\",\"retrieval_config_version\":\"lexical-v1\",\"candidates\":[]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
