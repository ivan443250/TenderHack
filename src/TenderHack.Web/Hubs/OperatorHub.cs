using Microsoft.AspNetCore.SignalR;
using TenderHack.Domain.Enums;

namespace TenderHack.Web.Hubs;

// Operator-facing queue. Group per support line so an operator only hears about their own line.
public sealed class OperatorHub : Hub
{
    public async Task JoinLine(SupportLine line) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, LineGroup(line));

    public static string LineGroup(SupportLine line) => $"line:{line}";
}
