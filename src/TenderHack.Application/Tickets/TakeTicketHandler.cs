using TenderHack.Application.Abstractions;
using TenderHack.Application.Common;

namespace TenderHack.Application.Tickets;

public sealed record TakeTicketCommand(Guid TicketId, Guid SpecialistId);

public sealed class TakeTicketHandler(
    ITicketRepository tickets,
    IUnitOfWork uow,
    IDateTimeProvider clock)
{
    public async Task<Result<bool>> HandleAsync(TakeTicketCommand command, CancellationToken ct)
    {
        var ticket = await tickets.GetWithMessagesAsync(command.TicketId, ct);
        if (ticket is null)
        {
            return Result<bool>.Failure("Ticket not found.");
        }

        var specialist = await tickets.GetSpecialistAsync(command.SpecialistId, ct);
        if (specialist is null)
        {
            return Result<bool>.Failure("Specialist not found.");
        }

        ticket.AssignTo(specialist, clock.Now);
        await uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
