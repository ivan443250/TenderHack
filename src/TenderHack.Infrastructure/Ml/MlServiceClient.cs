using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TenderHack.Application.Abstractions;
using TenderHack.Domain.Enums;
using TenderHack.Domain.ValueObjects;

namespace TenderHack.Infrastructure.Ml;

// Adapter over the ml FastAPI service (section 5). HttpClient is registered with a 5s timeout
// and Polly retry/circuit-breaker in the composition root. Never throws outward — on any
// failure this degrades to a safe "no answer" / "not profane, Other line" verdict so a downed
// ml service looks like a normal escalation to the operator, not a crash (section 10).
public sealed class MlServiceClient(HttpClient http, ILogger<MlServiceClient> logger) : IMlService
{
    public async Task<TriageResult> TriageAsync(string text, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/triage", new { text }, ct);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<TriageResponseDto>(ct)
                      ?? throw new InvalidOperationException("Empty /triage response.");

            return new TriageResult(
                dto.is_profane,
                dto.toxicity_score,
                Enum.TryParse<SupportLine>(dto.line, ignoreCase: true, out var line) ? line : SupportLine.Other,
                dto.line_confidence,
                dto.topic);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ml /triage failed, degrading to safe default");
            return new TriageResult(false, 0, SupportLine.Other, 0, null);
        }
    }

    public async Task<AnswerResult> AnswerAsync(string query, IReadOnlyList<string> history, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/answer", new { query, history }, ct);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<AnswerResponseDto>(ct)
                      ?? throw new InvalidOperationException("Empty /answer response.");

            var sources = dto.sources
                .Select(s => new SourceRef(s.chunk_id, s.document_title, s.excerpt, s.score))
                .ToList();

            return new AnswerResult(dto.answer, sources, dto.confidence, dto.no_answer, dto.timings);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ml /answer failed, degrading to no_answer");
            return new AnswerResult(string.Empty, [], 0, true, new Dictionary<string, double>());
        }
    }

    public async Task<IReadOnlyList<ProblemCluster>> ClusterFeedbackAsync(
        IReadOnlyList<FeedbackText> items, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                items = items.Select(i => new { ticket_id = i.TicketId, comment = i.Comment })
            };
            var response = await http.PostAsJsonAsync("/analytics/feedback", payload, ct);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<List<ProblemCluster>>(ct);
            return dto ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ml /analytics/feedback failed, degrading to empty clusters");
            return [];
        }
    }

    private sealed record TriageResponseDto(
        bool is_profane, double toxicity_score, string line, double line_confidence, string? topic);

    private sealed record AnswerResponseDto(
        string answer, List<SourceDto> sources, double confidence, bool no_answer,
        Dictionary<string, double> timings);

    private sealed record SourceDto(Guid chunk_id, string document_title, string excerpt, double score);
}
