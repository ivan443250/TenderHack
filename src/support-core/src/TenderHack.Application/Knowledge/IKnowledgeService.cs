namespace TenderHack.Application.Knowledge;

/// <summary>
/// Port for the frozen `knowledge-v0` boundary (knowledge-v0.md §5). Mirrors the orchestration
/// stage table 1:1; the generated NSwag client implementing this lives only in
/// TenderHack.Infrastructure and never leaks its own types past this interface.
/// </summary>
public interface IKnowledgeService
{
    Task<UnderstandResult> UnderstandAsync(UnderstandRequest request, KnowledgeRequestContext context, CancellationToken ct);

    Task<ModerationContextResult> AssessModerationContextAsync(ModerationContextRequest request, KnowledgeRequestContext context, CancellationToken ct);

    Task<RetrieveResult> RetrieveAsync(RetrieveRequest request, KnowledgeRequestContext context, CancellationToken ct);

    Task<AnswerabilityResult> AssessAnswerabilityAsync(AnswerabilityRequest request, KnowledgeRequestContext context, CancellationToken ct);

    Task<DraftResult> DraftAsync(DraftRequest request, KnowledgeRequestContext context, CancellationToken ct);

    Task<VerifyResult> VerifyAsync(VerifyRequest request, KnowledgeRequestContext context, CancellationToken ct);

    Task<SourceFragment> GetSourceAsync(string fragmentId, CancellationToken ct);
}
