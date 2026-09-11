using TenderHack.Application.Abstractions;
using TenderHack.Application.Common;

namespace TenderHack.Application.Tickets;

public sealed record ResolveTicketCommand(Guid TicketId);

public sealed class ResolveTicketHandler(
    ITicketRepository tickets,
    IUnitOfWork uow,
    IDateTimeProvider clock)
{
    public async Task<Result<bool>> HandleAsync(ResolveTicketCommand command, CancellationToken ct)
    {
        var ticket = await tickets.GetWithMessagesAsync(command.TicketId, ct);
        if (ticket is null)
        {
            return Result<bool>.Failure("Ticket not found.");
        }

        ticket.Resolve(clock.Now);
        await uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
