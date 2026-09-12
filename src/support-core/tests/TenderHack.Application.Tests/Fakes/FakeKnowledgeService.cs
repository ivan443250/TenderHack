using TenderHack.Application.Knowledge;

namespace TenderHack.Application.Tests.Fakes;

/// <summary>Stub-mode fake mirroring knowledge-v0.md §7: deterministic, configurable per test.</summary>
public sealed class FakeKnowledgeService : IKnowledgeService
{
    public Func<UnderstandRequest, UnderstandResult>? UnderstandOverride { get; set; }
    public AnswerabilityResult Answerability { get; set; } =
        new(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
    public DraftResult Draft { get; set; } =
        new("Ответ.", [new DraftClaim("claim-1", "Ответ.", ["frag-1"])], "stub-v0");
    public VerifyResult Verify { get; set; } =
        new([new ClaimVerification("claim-1", Supported: true, ["frag-1"])]);
    public KnowledgeFailureException? FailAt { get; set; }
    public string FailingStage { get; set; } = string.Empty;
    public ModerationContextResult ModerationContext { get; set; } = new(ModerationAmbiguity.Uncertain, "stub-v0");

    public Task<UnderstandResult> UnderstandAsync(UnderstandRequest request, KnowledgeRequestContext context, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(UnderstandAsync));
        var result = UnderstandOverride?.Invoke(request)
            ?? new UnderstandResult(request.Text, [], [], []);
        return Task.FromResult(result);
    }

    public Task<ModerationContextResult> AssessModerationContextAsync(ModerationContextRequest request, KnowledgeRequestContext context, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(AssessModerationContextAsync));
        return Task.FromResult(ModerationContext);
    }

    public Task<RetrieveResult> RetrieveAsync(RetrieveRequest request, KnowledgeRequestContext context, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(RetrieveAsync));
        return Task.FromResult(new RetrieveResult("snapshot-1", "stub-v0", [new RetrievalCandidate("frag-1", "doc-1", 1, null)]));
    }

    public Task<AnswerabilityResult> AssessAnswerabilityAsync(AnswerabilityRequest request, KnowledgeRequestContext context, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(AssessAnswerabilityAsync));
        return Task.FromResult(Answerability);
    }

    public Task<DraftResult> DraftAsync(DraftRequest request, KnowledgeRequestContext context, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(DraftAsync));
        return Task.FromResult(Draft);
    }

    public Task<VerifyResult> VerifyAsync(VerifyRequest request, KnowledgeRequestContext context, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(VerifyAsync));
        return Task.FromResult(Verify);
    }

    public Task<SourceFragment> GetSourceAsync(string fragmentId, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(GetSourceAsync));
        return Task.FromResult(new SourceFragment("doc-1", "Title", "v1", 1, null, "text", "snapshot-1"));
    }

    private void ThrowIfConfiguredToFail(string stage)
    {
        if (FailAt is not null && FailingStage == stage)
        {
            throw FailAt;
        }
    }
}
