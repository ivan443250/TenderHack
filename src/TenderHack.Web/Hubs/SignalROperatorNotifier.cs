using Microsoft.AspNetCore.SignalR;
using TenderHack.Application.Abstractions;
using TenderHack.Domain.Enums;

namespace TenderHack.Web.Hubs;

public sealed class SignalROperatorNotifier(IHubContext<OperatorHub> hub) : IOperatorNotifier
{
    public Task NotifyQueueAsync(SupportLine line, Guid ticketId, CancellationToken ct) =>
        hub.Clients.Group(OperatorHub.LineGroup(line))
            .SendAsync("queueUpdated", new { line, ticketId }, ct);
}
