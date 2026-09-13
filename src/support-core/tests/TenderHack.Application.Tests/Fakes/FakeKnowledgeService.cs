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
    public Exception? FailAt { get; set; }
    public string FailingStage { get; set; } = string.Empty;
    public ModerationContextResult ModerationContext { get; set; } = new(ModerationAmbiguity.Uncertain, "stub-v0");
    public ModerationContextRequest? LastModerationContextRequest { get; private set; }

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
        LastModerationContextRequest = request;
        return Task.FromResult(ModerationContext);
    }

    public RetrieveRequest? LastRetrieveRequest { get; private set; }
    public List<RetrieveRequest> RetrieveRequests { get; } = [];

    /// <summary>When set, returned by the first call only; subsequent calls fall back to the default candidate (B4's expand-once retry needs two distinguishable responses).</summary>
    public RetrieveResult? FirstRetrieveResult { get; set; }

    public Task<RetrieveResult> RetrieveAsync(RetrieveRequest request, KnowledgeRequestContext context, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(RetrieveAsync));
        LastRetrieveRequest = request;
        RetrieveRequests.Add(request);
        var result = RetrieveRequests.Count == 1 && FirstRetrieveResult is { } first
            ? first
            : new RetrieveResult("snapshot-1", "stub-v0", [new RetrievalCandidate("frag-1", "doc-1", 1, null)]);
        return Task.FromResult(result);
    }

    /// <summary>When set, dequeued one result per call (B4's expand-once retry needs "insufficient then sufficient"); falls back to <see cref="Answerability"/> once exhausted or unset.</summary>
    public Queue<AnswerabilityResult>? AnswerabilitySequence { get; set; }

    public Task<AnswerabilityResult> AssessAnswerabilityAsync(AnswerabilityRequest request, KnowledgeRequestContext context, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(AssessAnswerabilityAsync));
        var result = AnswerabilitySequence is { Count: > 0 } queue ? queue.Dequeue() : Answerability;
        return Task.FromResult(result);
    }

    public List<DraftRequest> DraftRequests { get; } = [];

    public Task<DraftResult> DraftAsync(DraftRequest request, KnowledgeRequestContext context, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(DraftAsync));
        DraftRequests.Add(request);
        return Task.FromResult(Draft);
    }

    /// <summary>When set, dequeued one result per call (B3's extractive-retry test needs "fail then succeed"); falls back to <see cref="Verify"/> once exhausted or unset.</summary>
    public Queue<VerifyResult>? VerifySequence { get; set; }

    public Task<VerifyResult> VerifyAsync(VerifyRequest request, KnowledgeRequestContext context, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(VerifyAsync));
        var result = VerifySequence is { Count: > 0 } queue ? queue.Dequeue() : Verify;
        return Task.FromResult(result);
    }

    public Task<SourceFragment> GetSourceAsync(string fragmentId, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(GetSourceAsync));
        return Task.FromResult(new SourceFragment("doc-1", "Title", "v1", 1, null, "text", "snapshot-1"));
    }

    public Materials Materials { get; set; } = new("snapshot-1", [new MaterialSummary("doc-1", "Инструкция.pdf", "v1", "2025-01-01", 10, 5)]);
    public MaterialSections MaterialSections { get; set; } = new("snapshot-1", "doc-1", [new MaterialSection("Раздел 1", 1, 3, "frag-1")]);

    public Task<Materials> ListMaterialsAsync(CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(ListMaterialsAsync));
        return Task.FromResult(Materials);
    }

    public Task<MaterialSections> ListMaterialSectionsAsync(string documentId, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(ListMaterialSectionsAsync));
        return Task.FromResult(MaterialSections);
    }

    public List<QualityTurnPush> PushedTurns { get; } = [];
    public List<QualityFeedbackPush> PushedFeedback { get; } = [];
    public List<QualityCompletionPush> PushedCompletions { get; } = [];
    public QualityEvaluations Evaluations { get; set; } = new("case-1", []);
    public IssueGroups IssueGroups { get; set; } = new([]);

    public Task PushQualityTurnAsync(QualityTurnPush push, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(PushQualityTurnAsync));
        PushedTurns.Add(push);
        return Task.CompletedTask;
    }

    public Task PushQualityFeedbackAsync(QualityFeedbackPush push, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(PushQualityFeedbackAsync));
        PushedFeedback.Add(push);
        return Task.CompletedTask;
    }

    public Task PushQualityCompletionAsync(QualityCompletionPush push, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(PushQualityCompletionAsync));
        PushedCompletions.Add(push);
        return Task.CompletedTask;
    }

    public Task<QualityEvaluations> GetQualityEvaluationsAsync(string caseId, CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(GetQualityEvaluationsAsync));
        return Task.FromResult(Evaluations);
    }

    public Task<IssueGroups> GetIssueGroupsAsync(CancellationToken ct)
    {
        ThrowIfConfiguredToFail(nameof(GetIssueGroupsAsync));
        return Task.FromResult(IssueGroups);
    }

    private void ThrowIfConfiguredToFail(string stage)
    {
        if (FailAt is not null && FailingStage == stage)
        {
            throw FailAt;
        }
    }
}
