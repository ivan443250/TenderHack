using TenderHack.Application.Abstractions;
using TenderHack.Application.Tickets;
using TenderHack.Domain.Enums;

namespace TenderHack.UnitTests.Application;

public sealed class ProcessUserMessageHandlerTests
{
    [Fact]
    public async Task Handle_ProfaneMessage_ClosesTicketAndSkipsAnswerCall()
    {
        var chat = new FakeChatNotifier();
        var repository = new FakeTicketRepository();
        var triage = new TriageResult(IsProfane: true, ToxicityScore: 0.9, SupportLine.Other, 0.5, null);
        var answer = new AnswerResult("should not be used", [], 1.0, false, new Dictionary<string, double>());
        var handler = new ProcessUserMessageHandler(
            repository, new FakeUnitOfWork(), new FakeMlService(triage, answer),
            chat, new FakeOperatorNotifier(), new FakeDateTimeProvider(), new FakeThresholdProvider());

        var result = await handler.HandleAsync(new ProcessUserMessageCommand(Guid.NewGuid(), "мат"), CancellationToken.None);

        Assert.IsType<ProcessResult.ClosedByViolationResult>(result);
        Assert.True(chat.ViolationNotified);
        Assert.False(chat.BotAnswerNotified);
        Assert.Equal(TicketStatus.ClosedByViolation, repository.Current!.Status);
    }

    [Fact]
    public async Task Handle_LowConfidenceAnswer_EscalatesToOperator()
    {
        var chat = new FakeChatNotifier();
        var operators = new FakeOperatorNotifier();
        var repository = new FakeTicketRepository();
        var triage = new TriageResult(false, 0.0, SupportLine.First, 0.8, "contracts");
        var answer = new AnswerResult(string.Empty, [], 0.2, NoAnswer: true, new Dictionary<string, double>());
        var handler = new ProcessUserMessageHandler(
            repository, new FakeUnitOfWork(), new FakeMlService(triage, answer),
            chat, operators, new FakeDateTimeProvider(), new FakeThresholdProvider());

        var result = await handler.HandleAsync(new ProcessUserMessageCommand(Guid.NewGuid(), "вопрос вне базы"), CancellationToken.None);

        var escalated = Assert.IsType<ProcessResult.Escalated>(result);
        Assert.Equal(SupportLine.First, escalated.Line);
        Assert.True(operators.QueueNotified);
        Assert.True(chat.AwaitingOperatorNotified);
        Assert.Equal(TicketStatus.AwaitingOperator, repository.Current!.Status);
    }

    [Fact]
    public async Task Handle_ConfidentAnswer_ReturnsBotAnswerAndMarksTicketAnswered()
    {
        var chat = new FakeChatNotifier();
        var repository = new FakeTicketRepository();
        var triage = new TriageResult(false, 0.0, SupportLine.First, 0.8, "contracts");
        var answer = new AnswerResult("Ответ по регламенту.", [], 0.9, false, new Dictionary<string, double>());
        var handler = new ProcessUserMessageHandler(
            repository, new FakeUnitOfWork(), new FakeMlService(triage, answer),
            chat, new FakeOperatorNotifier(), new FakeDateTimeProvider(), new FakeThresholdProvider());

        var result = await handler.HandleAsync(new ProcessUserMessageCommand(Guid.NewGuid(), "как продлить контракт"), CancellationToken.None);

        var answered = Assert.IsType<ProcessResult.Answered>(result);
        Assert.Equal("Ответ по регламенту.", answered.Answer.Text);
        Assert.True(chat.BotAnswerNotified);
        Assert.Equal(TicketStatus.BotAnswered, repository.Current!.Status);
    }
}
