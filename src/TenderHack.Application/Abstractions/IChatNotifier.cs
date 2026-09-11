using TenderHack.Domain.ValueObjects;

namespace TenderHack.Application.Abstractions;

// Adapter over ChatHub (SignalR), implemented in Web — see section 9.5.
public interface IChatNotifier
{
    Task NotifyViolationAsync(Guid ticketId, CancellationToken ct);

    Task NotifyBotAnswerAsync(Guid ticketId, string answer, IReadOnlyList<SourceRef> sources, CancellationToken ct);

    Task NotifyAwaitingOperatorAsync(Guid ticketId, CancellationToken ct);

    Task NotifyOperatorMessageAsync(Guid ticketId, string text, CancellationToken ct);

    Task StreamAnswerTokenAsync(Guid ticketId, string token, CancellationToken ct);

    Task StreamStageAsync(Guid ticketId, string stage, CancellationToken ct);
}
