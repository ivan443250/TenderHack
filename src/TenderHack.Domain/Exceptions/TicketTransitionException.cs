using TenderHack.Domain.Enums;

namespace TenderHack.Domain.Exceptions;

public sealed class TicketTransitionException(TicketStatus currentStatus, string attemptedTransition)
    : Exception($"Ticket in status '{currentStatus}' cannot perform transition '{attemptedTransition}'.")
{
    public TicketStatus CurrentStatus { get; } = currentStatus;
    public string AttemptedTransition { get; } = attemptedTransition;
}
