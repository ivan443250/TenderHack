namespace TenderHack.Application.Abstractions;

// Adapter contract only — no HTTP/NSwag types leak through this interface.
// MlServiceClient (Infrastructure) implements it and never throws outward: see section 10 (degradation).
public interface IMlService
{
    Task<TriageResult> TriageAsync(string text, CancellationToken ct);

    Task<AnswerResult> AnswerAsync(string query, IReadOnlyList<string> history, CancellationToken ct);

    Task<IReadOnlyList<ProblemCluster>> ClusterFeedbackAsync(IReadOnlyList<FeedbackText> items, CancellationToken ct);
}
