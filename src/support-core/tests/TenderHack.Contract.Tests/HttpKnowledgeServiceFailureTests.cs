using System.Diagnostics;
using System.Net;
using TenderHack.Application.Knowledge;
using TenderHack.Domain.Cases;
using TenderHack.Infrastructure.KnowledgeClient;
using Generated = TenderHack.Infrastructure.KnowledgeClient.Generated;
using Xunit;

namespace TenderHack.Contract.Tests;

/// <summary>
/// The single funnel from a failed `knowledge` HTTP call to <see cref="KnowledgeFailureException"/>
/// (knowledge-v0.md §3), exercised against a real <see cref="HttpClient"/> so the exception shapes
/// are the ones the runtime actually throws — not hand-built stand-ins.
/// </summary>
public sealed class HttpKnowledgeServiceFailureTests
{
    private static readonly KnowledgeRequestContext Context = new(Guid.NewGuid(), CaseId.New(), TurnId.New());

    private static HttpKnowledgeService CreateSut(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://knowledge.invalid"),
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        };

        return new HttpKnowledgeService(new Generated.KnowledgeApiClient(httpClient));
    }

    [Fact]
    public async Task HttpClientTimeoutIsClassifiedAsTimeoutNotLeakedAsTaskCanceled()
    {
        // Regression: the filter used to be `!ex.CancellationToken.IsCancellationRequested`, but the
        // TaskCanceledException HttpClient throws on its own Timeout carries its internal (already
        // cancelled) linked token — so the timeout escaped as a raw TaskCanceledException, became a
        // 500 in the api and took the whole api-worker host down.
        var sut = CreateSut(new HangingHandler(), timeout: TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAsync<KnowledgeFailureException>(() =>
            sut.UnderstandAsync(new UnderstandRequest("вопрос", null), Context, CancellationToken.None));

        Assert.Equal(KnowledgeFailureCategory.Timeout, ex.Category);
    }

    [Fact]
    public async Task CallerCancellationIsNotReinterpretedAsAKnowledgeFailure()
    {
        var sut = CreateSut(new HangingHandler());
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.UnderstandAsync(new UnderstandRequest("вопрос", null), Context, cts.Token));
    }

    [Fact]
    public async Task ConnectionFailureIsClassifiedAsUnavailable()
    {
        var sut = CreateSut(new ThrowingHandler(new HttpRequestException("connection refused")));

        var ex = await Assert.ThrowsAsync<KnowledgeFailureException>(() =>
            sut.UnderstandAsync(new UnderstandRequest("вопрос", null), Context, CancellationToken.None));

        Assert.Equal(KnowledgeFailureCategory.Unavailable, ex.Category);
    }

    [Fact]
    public async Task UndeclaredServerErrorIsClassifiedAsUnavailable()
    {
        var sut = CreateSut(new StatusHandler(HttpStatusCode.BadGateway));

        var ex = await Assert.ThrowsAsync<KnowledgeFailureException>(() =>
            sut.UnderstandAsync(new UnderstandRequest("вопрос", null), Context, CancellationToken.None));

        Assert.Equal(KnowledgeFailureCategory.Unavailable, ex.Category);
    }

    /// <summary>Never completes until the token it is given fires — the shape of a stalled upstream.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("upstream error") });
    }
}
