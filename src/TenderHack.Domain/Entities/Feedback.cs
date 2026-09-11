namespace TenderHack.Domain.Entities;

public sealed class Feedback
{
    public Guid Id { get; private set; }
    public Guid TicketId { get; private set; }
    public bool IsPositive { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset At { get; private set; }

    private Feedback() { }

    internal Feedback(Guid id, Guid ticketId, bool isPositive, string? comment, DateTimeOffset at)
    {
        Id = id;
        TicketId = ticketId;
        IsPositive = isPositive;
        Comment = comment;
        At = at;
    }
}
