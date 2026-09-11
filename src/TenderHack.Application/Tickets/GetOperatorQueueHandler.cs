using TenderHack.Application.Abstractions;
using TenderHack.Domain.Enums;

namespace TenderHack.Application.Tickets;

public sealed record GetOperatorQueueQuery(SupportLine Line);

public sealed class GetOperatorQueueHandler(ITicketRepository tickets)
{
    public async Task<IReadOnlyList<TicketSummary>> HandleAsync(GetOperatorQueueQuery query, CancellationToken ct)
    {
        var queue = await tickets.GetQueueAsync(query.Line, ct);
        return queue
            .Select(t => new TicketSummary(t.Id, t.Status, t.AssignedLine, t.UpdatedAt))
            .ToList();
    }
}
