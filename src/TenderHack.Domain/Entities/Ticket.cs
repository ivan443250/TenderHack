using TenderHack.Domain.Enums;
using TenderHack.Domain.Exceptions;
using TenderHack.Domain.ValueObjects;

namespace TenderHack.Domain.Entities;

public sealed class Ticket
{
    private readonly List<Message> _messages = [];
    private Feedback? _feedback;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public TicketStatus Status { get; private set; }
    public SupportLine? AssignedLine { get; private set; }
    public Guid? SpecialistId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<Message> Messages => _messages;
    public Feedback? Feedback => _feedback;

    public bool IsClosed => Status is TicketStatus.Resolved or TicketStatus.ClosedByViolation;

    private Ticket() { }

    public static Ticket Create(Guid id, Guid userId, DateTimeOffset at) => new()
    {
        Id = id,
        UserId = userId,
        Status = TicketStatus.New,
        CreatedAt = at,
        UpdatedAt = at
    };

    public Message AddUserMessage(string text, DateTimeOffset at)
    {
        if (IsClosed)
        {
            throw new TicketTransitionException(Status, nameof(AddUserMessage));
        }

        var message = new Message(Guid.NewGuid(), Id, MessageAuthor.User, text, at);
        _messages.Add(message);
        UpdatedAt = at;
        return message;
    }

    public Message AddOperatorMessage(string text, DateTimeOffset at)
    {
        if (Status is not TicketStatus.InProgress)
        {
            throw new TicketTransitionException(Status, nameof(AddOperatorMessage));
        }

        var message = new Message(Guid.NewGuid(), Id, MessageAuthor.Operator, text, at);
        _messages.Add(message);
        UpdatedAt = at;
        return message;
    }

    public IReadOnlyList<string> RecentHistory(int take = 6) =>
        _messages.TakeLast(take).Select(m => m.Text).ToList();

    public void AttachAnalysisToLastMessage(MessageAnalysis analysis) =>
        _messages[^1].AttachAnalysis(analysis);

    public void AnswerByBot(string answer, IReadOnlyList<SourceRef> sources, DateTimeOffset at)
    {
        if (IsClosed)
        {
            throw new TicketTransitionException(Status, nameof(AnswerByBot));
        }

        var message = new Message(Guid.NewGuid(), Id, MessageAuthor.Bot, answer, at);
        message.AttachSources(sources);
        _messages.Add(message);
        Status = TicketStatus.BotAnswered;
        UpdatedAt = at;
    }

    public void EscalateTo(SupportLine line, DateTimeOffset at)
    {
        if (IsClosed)
        {
            throw new TicketTransitionException(Status, nameof(EscalateTo));
        }

        AssignedLine = line;
        Status = TicketStatus.AwaitingOperator;
        UpdatedAt = at;
    }

    public void CloseForViolation(DateTimeOffset at)
    {
        if (IsClosed)
        {
            throw new TicketTransitionException(Status, nameof(CloseForViolation));
        }

        Status = TicketStatus.ClosedByViolation;
        UpdatedAt = at;
    }

    public void AssignTo(Specialist specialist, DateTimeOffset at)
    {
        if (Status is not TicketStatus.AwaitingOperator)
        {
            throw new TicketTransitionException(Status, nameof(AssignTo));
        }

        if (AssignedLine is not null && specialist.Line != AssignedLine)
        {
            throw new TicketTransitionException(Status, nameof(AssignTo));
        }

        SpecialistId = specialist.Id;
        Status = TicketStatus.InProgress;
        UpdatedAt = at;
    }

    public void Resolve(DateTimeOffset at)
    {
        if (Status is not (TicketStatus.InProgress or TicketStatus.BotAnswered))
        {
            throw new TicketTransitionException(Status, nameof(Resolve));
        }

        Status = TicketStatus.Resolved;
        UpdatedAt = at;
    }

    public void AddFeedback(bool isPositive, string? comment, DateTimeOffset at)
    {
        if (Status is not TicketStatus.Resolved)
        {
            throw new TicketTransitionException(Status, nameof(AddFeedback));
        }

        if (_feedback is not null)
        {
            throw new TicketTransitionException(Status, nameof(AddFeedback));
        }

        _feedback = new Feedback(Guid.NewGuid(), Id, isPositive, comment, at);
        UpdatedAt = at;
    }
}
