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
