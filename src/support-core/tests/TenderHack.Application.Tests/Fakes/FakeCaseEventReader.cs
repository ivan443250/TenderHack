using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.Tests.Fakes;

/// <summary>In-memory `case_events` log for tests that need `PrepareHandoffUseCase`/`HandoffPackageBuilder` to see real history.</summary>
public sealed class FakeCaseEventReader : ICaseEventReader
{
    private long _nextEventId = 1;
    private readonly List<PersistedCaseEvent> _events = [];

    public PersistedCaseEvent Add(string type, string payloadJson, TurnId? turnId = null, int? revision = null)
    {
        var e = new PersistedCaseEvent(_nextEventId++, turnId, revision, type, DateTimeOffset.UtcNow, payloadJson);
        _events.Add(e);
        return e;
    }

    public Task<IReadOnlyList<PersistedCaseEvent>> ListAsync(CaseId caseId, long after, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PersistedCaseEvent>>([.. _events.Where(e => e.EventId > after)]);

    public Task<IReadOnlyDictionary<CaseId, string>> ListFirstUserMessageTextsAsync(
        IReadOnlyCollection<CaseId> caseIds,
        CancellationToken ct)
    {
        var first = _events.FirstOrDefault(e => e.Type == "USER_MESSAGE");
        if (first is null)
        {
            return Task.FromResult<IReadOnlyDictionary<CaseId, string>>(new Dictionary<CaseId, string>());
        }

        using var payload = System.Text.Json.JsonDocument.Parse(first.PayloadJson);
        var text = payload.RootElement.GetProperty("text").GetString() ?? "";
        return Task.FromResult<IReadOnlyDictionary<CaseId, string>>(
            caseIds.ToDictionary(caseId => caseId, _ => text));
    }
}
