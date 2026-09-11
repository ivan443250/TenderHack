using TenderHack.Domain.Enums;
using TenderHack.Domain.ValueObjects;

namespace TenderHack.Domain.Entities;

public sealed class Message
{
    public Guid Id { get; private set; }
    public Guid TicketId { get; private set; }
    public MessageAuthor Author { get; private set; }
    public string Text { get; private set; } = null!;
    public DateTimeOffset At { get; private set; }
    public MessageAnalysis? Analysis { get; private set; }
    public IReadOnlyList<SourceRef> Sources { get; private set; } = [];

    private Message() { }

    internal Message(Guid id, Guid ticketId, MessageAuthor author, string text, DateTimeOffset at)
    {
        Id = id;
        TicketId = ticketId;
        Author = author;
        Text = text;
        At = at;
    }

    internal void AttachAnalysis(MessageAnalysis analysis) => Analysis = analysis;

    internal void AttachSources(IReadOnlyList<SourceRef> sources) => Sources = sources;
}
