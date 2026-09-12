using System.Net.Http.Json;
using System.Text.Json;
using TenderHack.Application.Knowledge;
using Xunit;

namespace TenderHack.Api.Tests;

/// <summary>
/// `evals/decisions` (quality.md §8): the real ASP.NET pipeline, driving `TurnOrchestrator` through
/// actual HTTP with `knowledge` in fixture/stub mode (knowledge-v0.md §7) — proves the wiring
/// (routes, JSON contract, DI, EF migrations against a real Postgres) that Application-layer
/// `TurnOrchestratorTests` cannot, since those call the orchestrator in-process. The decision
/// branches themselves are exhaustively covered there; this suite only needs one representative
/// fixture per branch to prove the HTTP path actually reaches it.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class DecisionFixtureTests(ApiTestFixture fixture)
{
    private async Task<(HttpClient Client, string CaseId)> CreateCaseAsync()
    {
        var client = fixture.CreateClient();
        var response = await client.PostAsync("/api/v0/cases", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (client, body.GetProperty("case_id").GetString()!);
    }

    private static async Task<JsonElement> SendMessageAsync(HttpClient client, string caseId, string text)
    {
        var response = await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task SufficientEvidenceNoRiskFlagsAnswers()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
        var (client, caseId) = await CreateCaseAsync();

        var response = await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text = "как подать заявку?" });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("ANSWER", result.GetProperty("decision").GetString());
        Assert.NotNull(result.GetProperty("answer").GetProperty("markdown").GetString());
        // architecture.md §18: technical trace for judges/team — every fresh (non-replayed) turn carries its own trace id.
        Assert.True(response.Headers.TryGetValues("X-Trace-Id", out var traceIds));
        Assert.True(Guid.TryParse(traceIds!.Single(), out _));
    }

    [Fact]
    public async Task SufficientEvidenceWithNegatedQueryFlagStillAnswersWithHandoff()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], ["NEGATED_QUERY"]);
        var (client, caseId) = await CreateCaseAsync();

        var result = await SendMessageAsync(client, caseId, "что если контракт НЕ подписан?");

        Assert.Equal("ANSWER_AND_HANDOFF", result.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task InsufficientEvidenceWithHumanSupportRequiredOffersHandoff()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Insufficient, [], [], ["HUMAN_SUPPORT_REQUIRED"]);
        var (client, caseId) = await CreateCaseAsync();

        var result = await SendMessageAsync(client, caseId, "почему статус не меняется больше часа?");

        Assert.Equal("HANDOFF_OFFER", result.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task InsufficientEvidenceWithNoEvidenceOffersHandoff()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Insufficient, [], [], []);
        var (client, caseId) = await CreateCaseAsync();

        var result = await SendMessageAsync(client, caseId, "неизвестный код РДИК_00000");

        Assert.Equal("HANDOFF_OFFER", result.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task ConditionDependentEvidenceClarifies()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.ConditionDependent, [], ["сумма контракта"], []);
        var (client, caseId) = await CreateCaseAsync();

        var result = await SendMessageAsync(client, caseId, "как аннулировать документ?");

        Assert.Equal("CLARIFY", result.GetProperty("decision").GetString());
        Assert.Contains("сумма контракта", result.GetProperty("missing_conditions").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task FailedVerificationOffersHandoffInsteadOfPublishingAnAnswer()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
        fixture.Knowledge.Verify = new VerifyResult([new ClaimVerification("claim-1", Supported: false, [])]);
        var (client, caseId) = await CreateCaseAsync();

        var result = await SendMessageAsync(client, caseId, "как подать заявку?");

        Assert.Equal("HANDOFF_OFFER", result.GetProperty("decision").GetString());
        // Reset for later tests in this sequential-within-class run.
        fixture.Knowledge.Verify = new VerifyResult([new ClaimVerification("claim-1", Supported: true, ["frag-1"])]);
    }

    [Fact]
    public async Task KnowledgeUnavailableRiskFlagProducesTechnicalError()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Insufficient, [], [], ["KNOWLEDGE_UNAVAILABLE"]);
        var (client, caseId) = await CreateCaseAsync();

        var result = await SendMessageAsync(client, caseId, "вопрос");

        Assert.Equal("TECHNICAL_ERROR", result.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task ExplicitHumanRequestOffersHandoffWithoutCallingKnowledge()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
        var (client, caseId) = await CreateCaseAsync();

        var result = await SendMessageAsync(client, caseId, "соедините меня с оператором");

        Assert.Equal("HANDOFF_OFFER", result.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task ProfanityOnceWarnsTwiceCloses()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
        var (client, caseId) = await CreateCaseAsync();

        var first = await SendMessageAsync(client, caseId, "ты сука тупая, помоги блин");
        Assert.Equal("MODERATION_WARNING", first.GetProperty("decision").GetString());

        var second = await SendMessageAsync(client, caseId, "опять бляха ничего не работает");
        Assert.Equal("MODERATION_CLOSE", second.GetProperty("decision").GetString());
    }

}
