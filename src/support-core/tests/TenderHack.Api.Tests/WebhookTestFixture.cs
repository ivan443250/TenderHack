using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TenderHack.Application.Knowledge;
using TenderHack.Application.Tests.Fakes;
using Xunit;

namespace TenderHack.Api.Tests;

/// <summary>
/// Separate host from <see cref="ApiTestFixture"/> because inbound channel B (support-adapter-v0.md
/// §6.2) is only mapped when `Support:Webhook:Enabled=true` — a setting the shared fixture
/// deliberately leaves off since no other test exercises it.
/// </summary>
public sealed class WebhookTestFixture : IAsyncLifetime
{
    public const string Secret = "webhook-test-secret";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tenderhack_webhook_test")
        .WithUsername("api_rw")
        .WithPassword("test_only")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            builder.UseSetting("Support:Webhook:Enabled", "true");
            builder.UseSetting("Support:Webhook:Secret", Secret);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IKnowledgeService>();
                services.AddSingleton<IKnowledgeService>(new FakeKnowledgeService());
            });
        });

        _ = _factory.Services;
    }

    public HttpClient CreateClient() => _factory!.CreateClient();

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class WebhookTestCollection : ICollectionFixture<WebhookTestFixture>
{
    public const string Name = "webhook-integration";
}
