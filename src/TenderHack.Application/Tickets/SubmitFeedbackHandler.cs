using TenderHack.Application.Abstractions;
using TenderHack.Application.Common;

namespace TenderHack.Application.Tickets;

public sealed record SubmitFeedbackCommand(Guid TicketId, bool IsPositive, string? Comment);

public sealed class SubmitFeedbackHandler(
    ITicketRepository tickets,
    IUnitOfWork uow,
    IDateTimeProvider clock)
{
    public async Task<Result<bool>> HandleAsync(SubmitFeedbackCommand command, CancellationToken ct)
    {
        var ticket = await tickets.GetWithMessagesAsync(command.TicketId, ct);
        if (ticket is null)
        {
            return Result<bool>.Failure("Ticket not found.");
        }

        ticket.AddFeedback(command.IsPositive, command.Comment, clock.Now);
        await uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
