using Microsoft.AspNetCore.SignalR;
using TenderHack.Application.Abstractions;
using TenderHack.Domain.ValueObjects;

namespace TenderHack.Web.Hubs;

public sealed class SignalRChatNotifier(IHubContext<ChatHub> hub) : IChatNotifier
{
    public Task NotifyViolationAsync(Guid ticketId, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.TicketGroup(ticketId))
            .SendAsync("violation", ticketId, ct);

    public Task NotifyBotAnswerAsync(Guid ticketId, string answer, IReadOnlyList<SourceRef> sources, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.TicketGroup(ticketId))
            .SendAsync("botAnswer", new { ticketId, answer, sources }, ct);

    public Task NotifyAwaitingOperatorAsync(Guid ticketId, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.TicketGroup(ticketId))
            .SendAsync("awaitingOperator", ticketId, ct);

    public Task NotifyOperatorMessageAsync(Guid ticketId, string text, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.TicketGroup(ticketId))
            .SendAsync("operatorMessage", new { ticketId, text }, ct);

    public Task StreamAnswerTokenAsync(Guid ticketId, string token, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.TicketGroup(ticketId))
            .SendAsync("answerToken", new { ticketId, token }, ct);

    public Task StreamStageAsync(Guid ticketId, string stage, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.TicketGroup(ticketId))
            .SendAsync("stage", new { ticketId, stage }, ct);
}
