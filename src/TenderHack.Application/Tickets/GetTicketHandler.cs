using TenderHack.Application.Abstractions;

namespace TenderHack.Application.Tickets;

public sealed record GetTicketQuery(Guid TicketId);

public sealed class GetTicketHandler(ITicketRepository tickets)
{
    public async Task<TicketView?> HandleAsync(GetTicketQuery query, CancellationToken ct)
    {
        var ticket = await tickets.GetWithMessagesAsync(query.TicketId, ct);
        if (ticket is null)
        {
            return null;
        }

        var messages = ticket.Messages
            .Select(m => new MessageView(m.Id, m.Author, m.Text, m.At, m.Sources))
            .ToList();

        return new TicketView(ticket.Id, ticket.Status, ticket.AssignedLine, messages);
    }
}
