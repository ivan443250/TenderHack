using TenderHack.Application.Ports;

namespace TenderHack.Application.Tests.Fakes;

public sealed class FakeOutbox : IOutbox
{
    public List<(string MessageType, object Payload)> Enqueued { get; } = [];

    public void Enqueue(string messageType, object payload) => Enqueued.Add((messageType, payload));
}
