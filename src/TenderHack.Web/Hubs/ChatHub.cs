using Microsoft.AspNetCore.SignalR;
using TenderHack.Application.Tickets;

namespace TenderHack.Web.Hubs;

// User-facing chat. Group per ticket so pushes reach only the participants of that conversation.
public sealed class ChatHub(ProcessUserMessageHandler processUserMessage) : Hub
{
    public async Task JoinTicket(Guid ticketId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, TicketGroup(ticketId));

    public async Task SendMessage(Guid userId, string text)
    {
        var result = await processUserMessage.HandleAsync(new ProcessUserMessageCommand(userId, text), Context.ConnectionAborted);
        _ = result;
    }

    public static string TicketGroup(Guid ticketId) => $"ticket:{ticketId}";
}
