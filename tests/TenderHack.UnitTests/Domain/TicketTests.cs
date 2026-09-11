using TenderHack.Domain.Entities;
using TenderHack.Domain.Enums;
using TenderHack.Domain.Exceptions;

namespace TenderHack.UnitTests.Domain;

public sealed class TicketTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UtcNow;

    [Fact]
    public void AddUserMessage_ThenAnswerByBot_SetsBotAnsweredStatus()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), Guid.NewGuid(), At);

        ticket.AddUserMessage("Как продлить контракт?", At);
        ticket.AnswerByBot("Ответ по регламенту.", [], At);

        Assert.Equal(TicketStatus.BotAnswered, ticket.Status);
    }

    [Fact]
    public void EscalateTo_SetsAwaitingOperatorAndAssignedLine()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), Guid.NewGuid(), At);
        ticket.AddUserMessage("Вопрос без ответа в базе", At);

        ticket.EscalateTo(SupportLine.First, At);

        Assert.Equal(TicketStatus.AwaitingOperator, ticket.Status);
        Assert.Equal(SupportLine.First, ticket.AssignedLine);
    }

    [Fact]
    public void CloseForViolation_SetsClosedByViolationStatus()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), Guid.NewGuid(), At);
        ticket.AddUserMessage("нецензурная лексика", At);

        ticket.CloseForViolation(At);

        Assert.Equal(TicketStatus.ClosedByViolation, ticket.Status);
    }

    [Fact]
    public void AddUserMessage_AfterClosedByViolation_Throws()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), Guid.NewGuid(), At);
        ticket.AddUserMessage("текст", At);
        ticket.CloseForViolation(At);

        Assert.Throws<TicketTransitionException>(() => ticket.AddUserMessage("ещё сообщение", At));
    }

    [Fact]
    public void AddFeedback_BeforeResolved_Throws()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), Guid.NewGuid(), At);
        ticket.AddUserMessage("текст", At);
        ticket.AnswerByBot("ответ", [], At);

        Assert.Throws<TicketTransitionException>(() => ticket.AddFeedback(true, null, At));
    }

    [Fact]
    public void AssignTo_WrongLine_Throws()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), Guid.NewGuid(), At);
        ticket.AddUserMessage("текст", At);
        ticket.EscalateTo(SupportLine.First, At);
        var specialist = new Specialist(Guid.NewGuid(), "Иван Иванов", SupportLine.Second);

        Assert.Throws<TicketTransitionException>(() => ticket.AssignTo(specialist, At));
    }

    [Fact]
    public void AddFeedback_Twice_Throws()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), Guid.NewGuid(), At);
        ticket.AddUserMessage("текст", At);
        ticket.AnswerByBot("ответ", [], At);
        ticket.Resolve(At);
        ticket.AddFeedback(true, "спасибо", At);

        Assert.Throws<TicketTransitionException>(() => ticket.AddFeedback(false, null, At));
    }
}
