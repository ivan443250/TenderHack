using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.Tests.Fakes;

public sealed class FakeTurnEventStream : ITurnEventStream
{
    private readonly List<(CaseId CaseId, CaseEvent Event)> _published = [];
    private long _nextEventId = 1;

    public IReadOnlyList<(CaseId CaseId, CaseEvent Event)> Published => _published;

    public Task<long> PublishAsync(CaseId caseId, CaseEvent @event, CancellationToken ct)
    {
        _published.Add((caseId, @event));
        return Task.FromResult(_nextEventId++);
    }
}
