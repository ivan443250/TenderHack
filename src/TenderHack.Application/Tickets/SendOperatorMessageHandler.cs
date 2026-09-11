using TenderHack.Application.Abstractions;
using TenderHack.Application.Common;

namespace TenderHack.Application.Tickets;

public sealed record SendOperatorMessageCommand(Guid TicketId, string Text);

public sealed class SendOperatorMessageHandler(
    ITicketRepository tickets,
    IUnitOfWork uow,
    IChatNotifier chat,
    IDateTimeProvider clock)
{
    public async Task<Result<bool>> HandleAsync(SendOperatorMessageCommand command, CancellationToken ct)
    {
        var ticket = await tickets.GetWithMessagesAsync(command.TicketId, ct);
        if (ticket is null)
        {
            return Result<bool>.Failure("Ticket not found.");
        }

        ticket.AddOperatorMessage(command.Text, clock.Now);
        await uow.SaveChangesAsync(ct);
        await chat.NotifyOperatorMessageAsync(ticket.Id, command.Text, ct);
        return Result<bool>.Success(true);
    }
}
