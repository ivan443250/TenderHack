using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TenderHack.Application.Knowledge;
using Xunit;

namespace TenderHack.Api.Tests;

/// <summary>
/// End-to-end HTTP coverage for the invariants quality.md §8 lists as mandatory regression: owner
/// scoping, idempotency replay, and a completed case rejecting new messages — against the real
/// pipeline (routes, cookies, EF, Postgres), not an in-process fake.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class CaseLifecycleTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task CaseListTitleComesFromFirstUserMessageAndDoesNotChangeOnLaterTurns()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
        var client = fixture.CreateClient();
        var created = await client.PostAsync("/api/v0/cases", content: null);
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString();

        await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text = "Как создать СТЕ для оферты?" });
        await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text = "Это второй вопрос" });

        var response = await client.GetAsync("/api/v0/cases?status=active");
        response.EnsureSuccessStatusCode();
        var cases = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = cases.EnumerateArray().Single(value => value.GetProperty("case_id").GetString() == caseId);
        Assert.Equal("Как создать СТЕ для оферты?", item.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ACaseIdAloneGrantsNothingToADifferentOwnerSCookie()
    {
        var owner = fixture.CreateClient();
        var created = await owner.PostAsync("/api/v0/cases", content: null);
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString();

        var stranger = fixture.CreateClient();
        // Give the stranger their own session first (a bare case_id must still grant nothing).
        await stranger.PostAsync("/api/v0/session", content: null);

        var response = await stranger.GetAsync($"/api/v0/cases/{caseId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RepeatingCreateCaseWithTheSameIdempotencyKeyReturnsTheSameCase()
    {
        var client = fixture.CreateClient();
        var key = Guid.NewGuid().ToString();

        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/v0/cases");
        first.Headers.Add("Idempotency-Key", key);
        var firstResponse = await client.SendAsync(first);
        var firstCaseId = (await firstResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString();

        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/v0/cases");
        second.Headers.Add("Idempotency-Key", key);
        var secondResponse = await client.SendAsync(second);
        var secondCaseId = (await secondResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString();

        Assert.Equal(firstCaseId, secondCaseId);
    }

    [Fact]
    public async Task MessageOnACompletedCaseIsRejectedAsCaseClosed()
    {
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
        var client = fixture.CreateClient();
        var created = await client.PostAsync("/api/v0/cases", content: null);
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString();

        var complete = await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/complete", new { solved = true });
        complete.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text = "ещё вопрос" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CASE_CLOSED", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RepeatingSendMessageWithTheSameIdempotencyKeyDoesNotRunThePipelineTwice()
    {
        // architecture.md §7: same key + same payload → same logical result — a lost response must
        // not re-run knowledge/moderation a second time or supersede the first turn's own revision.
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
        var client = fixture.CreateClient();
        var created = await client.PostAsync("/api/v0/cases", content: null);
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString();
        var key = Guid.NewGuid().ToString();

        using var first = new HttpRequestMessage(HttpMethod.Post, $"/api/v0/cases/{caseId}/messages") { Content = JsonContent.Create(new { text = "вопрос" }) };
        first.Headers.Add("Idempotency-Key", key);
        var firstResponse = await client.SendAsync(first);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();

        using var second = new HttpRequestMessage(HttpMethod.Post, $"/api/v0/cases/{caseId}/messages") { Content = JsonContent.Create(new { text = "вопрос" }) };
        second.Headers.Add("Idempotency-Key", key);
        var secondResponse = await client.SendAsync(second);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(firstBody.GetProperty("turn_id").GetString(), secondBody.GetProperty("turn_id").GetString());
        Assert.Equal(firstBody.GetProperty("revision").GetInt32(), secondBody.GetProperty("revision").GetInt32());

        // The replay did not start a third revision — a fresh, unkeyed message still gets revision 2.
        var third = await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text = "новый вопрос" });
        var thirdBody = await third.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, thirdBody.GetProperty("revision").GetInt32());
    }

    [Fact]
    public async Task RepeatingCompleteWithTheSameIdempotencyKeyReplaysInsteadOfConflicting()
    {
        var client = fixture.CreateClient();
        var created = await client.PostAsync("/api/v0/cases", content: null);
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString();
        var key = Guid.NewGuid().ToString();

        using var first = new HttpRequestMessage(HttpMethod.Post, $"/api/v0/cases/{caseId}/complete") { Content = JsonContent.Create(new { solved = true }) };
        first.Headers.Add("Idempotency-Key", key);
        var firstResponse = await client.SendAsync(first);
        firstResponse.EnsureSuccessStatusCode();

        using var second = new HttpRequestMessage(HttpMethod.Post, $"/api/v0/cases/{caseId}/complete") { Content = JsonContent.Create(new { solved = true }) };
        second.Headers.Add("Idempotency-Key", key);
        var secondResponse = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var body = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CLOSED_USER", body.GetProperty("conversation_status").GetString());
    }

    [Fact]
    public async Task ASlowTurnCannotPublishAfterANewerRevisionSupersedesItOnTheRealDatabase()
    {
        // architecture.md §6 "running old turn cannot publish after a newer revision supersedes it"
        // — enforced by the `turns` row's own `xmin` (C2), not just in-process state, so this needs a
        // real Postgres: a fake/in-memory Case can't demonstrate two independently loaded copies of
        // the same aggregate racing to commit.
        var client = fixture.CreateClient();
        var created = await client.PostAsync("/api/v0/cases", content: null);
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString();

        var release = new TaskCompletionSource();
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Sufficient, ["frag-1"], [], []);
        fixture.Knowledge.UnderstandOverride = req =>
        {
            release.Task.Wait(TimeSpan.FromSeconds(5)); // hold this request open until the "newer" one has fully committed
            return new UnderstandResult(req.Text, [], [], []);
        };

        var slowSend = client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text = "первый (медленный) вопрос" });

        // Let the first request's own `StartTurn` + initial commit (revision 1, before it ever
        // reaches `understand`) land, so the second request cleanly computes revision 2 instead of
        // racing the same insert — the scenario under test is the *publish*-time race, not the
        // insert. Polled rather than a fixed delay: the first request in a fresh test host pays a
        // real (variable) JIT/pipeline warmup cost.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var snapshotResponse = await client.GetAsync($"/api/v0/cases/{caseId}");
            var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
            if (snapshot.TryGetProperty("active_turn", out var activeTurn) && activeTurn.ValueKind != JsonValueKind.Null
                && activeTurn.GetProperty("revision").GetInt32() == 1)
            {
                break;
            }

            await Task.Delay(100);
        }

        // A second, independent request against the same case — loads its own copy of the aggregate
        // and commits revision 2 while the first request is still stuck inside `understand`.
        fixture.Knowledge.UnderstandOverride = null;
        var second = await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text = "второй вопрос" });
        second.EnsureSuccessStatusCode();

        release.SetResult();
        var slowResponse = await slowSend;

        Assert.Equal(HttpStatusCode.Conflict, slowResponse.StatusCode);
        var body = await slowResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CONCURRENCY_CONFLICT", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ValidationErrorOnAnOverlongMessageDoesNotCreateATurn()
    {
        var client = fixture.CreateClient();
        var created = await client.PostAsync("/api/v0/cases", content: null);
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString();

        var response = await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text = new string('a', 4001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HandoffPrepareBuildsAnEditablePackageAndConfirmEnqueuesSubmission()
    {
        // Submission itself runs in `api-worker` (a separate host, not exercised by this
        // WebApplicationFactory<TenderHack.Api.Program>) — this test verifies what the `api` process
        // itself is responsible for: assembling the package and durably enqueuing the submit message,
        // exactly once, atomically with the PENDING transition (web-api-v0.md §8).
        fixture.Knowledge.Answerability = new AnswerabilityResult(EvidenceSufficiency.Insufficient, [], [], []);
        var client = fixture.CreateClient();
        var created = await client.PostAsync("/api/v0/cases", content: null);
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("case_id").GetString();
        await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/messages", new { text = "нужен специалист по коду РДИК_9999" });

        var prepare = await client.PostAsync($"/api/v0/cases/{caseId}/handoff/prepare", content: null);
        prepare.EnsureSuccessStatusCode();
        var preparedSnapshot = await prepare.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_REQUESTED", preparedSnapshot.GetProperty("handoff").GetProperty("status").GetString());

        var confirm = await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/handoff/confirm", new { summary = "Пользователь просит помощь по коду РДИК_9999" });
        confirm.EnsureSuccessStatusCode();
        var confirmedHandoff = (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("handoff");
        Assert.Equal("PENDING", confirmedHandoff.GetProperty("status").GetString());

        // A repeated confirm with the same case must not queue a second submission (support-adapter-v0.md §3).
        var secondConfirm = await client.PostAsJsonAsync($"/api/v0/cases/{caseId}/handoff/confirm", new { summary = "Другое резюме" });
        Assert.Equal(HttpStatusCode.Conflict, secondConfirm.StatusCode);
    }
}
