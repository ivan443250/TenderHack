using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Options;
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

        // Per-stage timeouts default to well above every scenario below, so these tests keep
        // exercising the outer `HttpClient.Timeout` / caller-cancellation paths exactly as before —
        // B5 (architecture.md §10) only adds a second, shorter timeout source, it does not replace this one.
        var options = Options.Create(new KnowledgeServiceOptions());
        return new HttpKnowledgeService(new Generated.KnowledgeApiClient(httpClient), options);
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
    public async Task PerStageTimeoutFiresIndependentlyOfTheSharedHttpClientTimeout()
    {
        // architecture.md §10: per-stage timeouts are api config, not one shared HttpClient.Timeout.
        // A generous HttpClient.Timeout must not let a short per-stage budget go unenforced.
        var httpClient = new HttpClient(new HangingHandler())
        {
            BaseAddress = new Uri("http://knowledge.invalid"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        var options = Options.Create(new KnowledgeServiceOptions
        {
            Timeouts = new KnowledgeStageTimeouts { Understand = TimeSpan.FromMilliseconds(100) },
        });
        var sut = new HttpKnowledgeService(new Generated.KnowledgeApiClient(httpClient), options);

        var stopwatch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<KnowledgeFailureException>(() =>
            sut.UnderstandAsync(new UnderstandRequest("вопрос", null), Context, CancellationToken.None));
        stopwatch.Stop();

        Assert.Equal(KnowledgeFailureCategory.Timeout, ex.Category);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"expected the 100ms stage timeout to fire, not the 30s HttpClient.Timeout (took {stopwatch.Elapsed})");
    }

    [Fact]
    public async Task DraftUsesItsOwnConfiguredTimeoutNotUnderstandsBudget()
    {
        // A slow generation stage (Draft) must read its own KnowledgeStageTimeouts member, not
        // whichever timeout happens to be configured for a different stage.
        var httpClient = new HttpClient(new HangingHandler())
        {
            BaseAddress = new Uri("http://knowledge.invalid"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        var options = Options.Create(new KnowledgeServiceOptions
        {
            Timeouts = new KnowledgeStageTimeouts
            {
                Understand = TimeSpan.FromSeconds(10), // deliberately much larger than Draft below
                Draft = TimeSpan.FromMilliseconds(100),
            },
        });
        var sut = new HttpKnowledgeService(new Generated.KnowledgeApiClient(httpClient), options);

        var stopwatch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<KnowledgeFailureException>(() =>
            sut.DraftAsync(new DraftRequest("вопрос", "snap-1", []), Context, CancellationToken.None));
        stopwatch.Stop();

        Assert.Equal(KnowledgeFailureCategory.Timeout, ex.Category);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"expected Draft's own 100ms timeout to fire (took {stopwatch.Elapsed})");
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
    public async Task ATransientConnectionFailureIsRetriedAndCanStillSucceed()
    {
        // architecture.md §7: understand is an idempotent stage — up to 2 retries of UNAVAILABLE/TIMEOUT.
        const string successBody = """{"normalized_text":"вопрос","entities":[],"exact_codes":[],"language_flags":{"detected_language":"ru","typo_corrected":false}}""";
        var handler = new FailThenSucceedHandler(failuresBeforeSuccess: 2, successBody);
        var sut = CreateSut(handler);

        var result = await sut.UnderstandAsync(new UnderstandRequest("вопрос", null), Context, CancellationToken.None);

        Assert.Equal("вопрос", result.NormalizedText);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task RetriesAreExhaustedAfterTheConfiguredAttemptCount()
    {
        var handler = new FailThenSucceedHandler(failuresBeforeSuccess: 10, successBody: "{}");
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<KnowledgeFailureException>(() =>
            sut.UnderstandAsync(new UnderstandRequest("вопрос", null), Context, CancellationToken.None));

        Assert.Equal(KnowledgeFailureCategory.Unavailable, ex.Category);
        // 3 total attempts (1 + 2 retries) for an idempotent stage — never more, never fewer.
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task ANonRetryableFailureIsNotRetriedAtAll()
    {
        // A 4xx (INVALID_RESPONSE-shaped) failure will not succeed on retry — retrying it would only
        // add latency for a guaranteed-repeat failure.
        var handler = new StatusHandler(HttpStatusCode.BadRequest);
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<KnowledgeFailureException>(() =>
            sut.UnderstandAsync(new UnderstandRequest("вопрос", null), Context, CancellationToken.None));

        Assert.Equal(KnowledgeFailureCategory.InvalidResponse, ex.Category);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task DraftGetsOnlyOneRetryNotTwo()
    {
        var handler = new FailThenSucceedHandler(failuresBeforeSuccess: 10, successBody: "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<KnowledgeFailureException>(() =>
            sut.DraftAsync(new DraftRequest("вопрос", "snap-1", []), Context, CancellationToken.None));

        // 2 total attempts (1 + 1 retry) — draft never gets the idempotent stages' full 3.
        Assert.Equal(2, handler.CallCount);
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
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("upstream error") });
        }
    }

    /// <summary>Fails with a transport exception the first <paramref name="failuresBeforeSuccess"/> calls, then returns a valid 2xx body — for proving a retry actually recovers.</summary>
    private sealed class FailThenSucceedHandler(int failuresBeforeSuccess, string successBody) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount <= failuresBeforeSuccess)
            {
                throw new HttpRequestException("transient connection reset");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(successBody, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
