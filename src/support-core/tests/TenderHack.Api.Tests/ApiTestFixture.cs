using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TenderHack.Application.Knowledge;
using TenderHack.Application.Ports;
using TenderHack.Application.Tests.Fakes;
using Xunit;

namespace TenderHack.Api.Tests;

/// <summary>
/// Real ASP.NET pipeline (`WebApplicationFactory&lt;Program&gt;`) against a disposable Postgres
/// container — the wiring/contract-level check `evals/decisions` calls for (quality.md §8): real
/// routes, real EF migrations/JSON serialization/idempotency, `knowledge` replaced by the same
/// deterministic <see cref="FakeKnowledgeService"/> the Application-layer orchestrator tests use, so
/// each fixture only has to state the `Decision`/`reason_codes` it expects, not fake an HTTP server.
/// One container for the whole test collection — migrations run once, tests share the schema but
/// never the same case row (each test creates its own case).
/// </summary>
public sealed class ApiTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tenderhack_test")
        .WithUsername("api_rw")
        .WithPassword("test_only")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public FakeKnowledgeService Knowledge { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            builder.ConfigureServices(services =>
            {
                // Real HTTP/EF/idempotency/SSE wiring stays untouched; only the two runtime
                // boundaries this suite does not exercise for their own sake are swapped for
                // deterministic fakes (architecture.md §11: knowledge-v0's fixture/stub mode).
                services.RemoveAll<IKnowledgeService>();
                services.AddSingleton<IKnowledgeService>(Knowledge);
            });
        });

        // Force the host to build now (and run its startup migrations) rather than lazily on the
        // first request, so a migration failure surfaces from InitializeAsync, not from a random test.
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
public sealed class ApiTestCollection : ICollectionFixture<ApiTestFixture>
{
    public const string Name = "api-integration";
}
