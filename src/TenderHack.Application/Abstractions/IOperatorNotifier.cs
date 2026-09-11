using TenderHack.Domain.Enums;

namespace TenderHack.Application.Abstractions;

// Adapter over OperatorHub (SignalR), implemented in Web — see section 9.5.
public interface IOperatorNotifier
{
    Task NotifyQueueAsync(SupportLine line, Guid ticketId, CancellationToken ct);
}
