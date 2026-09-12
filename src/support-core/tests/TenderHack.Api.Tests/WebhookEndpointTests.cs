using System.Net;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace TenderHack.Api.Tests;

/// <summary>
/// support-adapter-v0.md §10 contract test 13: unsigned / bad signature / stale timestamp must
/// answer `401` with no persisted state change, and the endpoint must not exist at all when the
/// webhook channel is disabled (the default — the demo adapter never calls it).
/// </summary>
[Collection(WebhookTestCollection.Name)]
public sealed class WebhookEndpointTests(WebhookTestFixture fixture)
{
    private const string Path = "/api/v0/integrations/support/status";

    [Fact]
    public async Task MissingSignatureIsUnauthorized()
    {
        var client = fixture.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Support-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongSignatureIsUnauthorized()
    {
        var client = fixture.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Support-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        request.Headers.Add("X-Support-Signature", "sha256=" + new string('0', 64));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StaleTimestampIsUnauthorizedEvenWithAValidSignature()
    {
        var client = fixture.CreateClient();
        const string body = "{}";
        var staleTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Support-Timestamp", staleTimestamp);
        request.Headers.Add("X-Support-Signature", Sign(body));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string Sign(string body)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookTestFixture.Secret), Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexStringLower(mac);
    }
}

/// <summary>The webhook route must not be registered at all when `Support:Webhook:Enabled` is left
/// at its default (false) — <see cref="ApiTestFixture"/> never sets it, matching production
/// default and the demo adapter's compose config.</summary>
[Collection(ApiTestCollection.Name)]
public sealed class WebhookEndpointDisabledTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task EndpointIsAbsentWhenWebhookIsDisabled()
    {
        var client = fixture.CreateClient();
        var response = await client.PostAsync(
            "/api/v0/integrations/support/status",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        // ASP.NET Core's routing answers `405` (not `404`) for a path that would match a mapped
        // route's template if only the HTTP method differed — MapFallbackToFile registers this same
        // path for GET so unmatched routers can serve the SPA (Program.cs), so an unmapped POST here
        // surfaces as 405. Either status proves the same thing the contract requires: no webhook
        // handler ever runs and nothing is persisted.
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Expected 404 or 405, got {response.StatusCode}");
    }
}
