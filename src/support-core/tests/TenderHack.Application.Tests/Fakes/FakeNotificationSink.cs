using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.Tests.Fakes;

public sealed class FakeNotificationSink : INotificationSink
{
    public List<(string OwnerId, CaseId CaseId, string Type, string Title, string Body, string? IntegrationMode, long SourceEventId)> Enqueued { get; } = [];

    public void Enqueue(string ownerId, CaseId caseId, string type, string title, string body, string? integrationMode, long sourceEventId) =>
        Enqueued.Add((ownerId, caseId, type, title, body, integrationMode, sourceEventId));
}
