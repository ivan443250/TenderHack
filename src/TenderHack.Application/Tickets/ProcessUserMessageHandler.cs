using TenderHack.Application.Abstractions;
using DomainMessageAnalysis = TenderHack.Domain.Entities.MessageAnalysis;

namespace TenderHack.Application.Tickets;

public sealed record ProcessUserMessageCommand(Guid UserId, string Text);

// Central pipeline: triage -> (violation | answer -> (escalate | answered)). See section 9.3.
public sealed class ProcessUserMessageHandler(
    ITicketRepository tickets,
    IUnitOfWork uow,
    IMlService ml,
    IChatNotifier chat,
    IOperatorNotifier operators,
    IDateTimeProvider clock,
    IThresholdProvider thresholds)
{
    public async Task<ProcessResult> HandleAsync(ProcessUserMessageCommand command, CancellationToken ct)
    {
        var ticket = await tickets.GetOrCreateAsync(command.UserId, ct);
        var message = ticket.AddUserMessage(command.Text, clock.Now);

        var triage = await ml.TriageAsync(command.Text, ct);
        var analysis = DomainMessageAnalysis.FromTriage(
            triage.IsProfane, triage.ToxicityScore, triage.Line, triage.LineConfidence, triage.Topic);
        ticket.AttachAnalysisToLastMessage(analysis);

        if (triage.IsProfane)
        {
            ticket.CloseForViolation(clock.Now);
            await uow.SaveChangesAsync(ct);
            await chat.NotifyViolationAsync(ticket.Id, ct);
            return ProcessResult.ClosedByViolation;
        }

        var answer = await ml.AnswerAsync(command.Text, ticket.RecentHistory(), ct);
        analysis.AttachAnswerVerdict(answer.Confidence, answer.NoAnswer);

        if (answer.NoAnswer || answer.Confidence < thresholds.MinConfidence)
        {
            ticket.EscalateTo(triage.Line, clock.Now);
            await uow.SaveChangesAsync(ct);
            await operators.NotifyQueueAsync(triage.Line, ticket.Id, ct);
            await chat.NotifyAwaitingOperatorAsync(ticket.Id, ct);
            return new ProcessResult.Escalated(triage.Line);
        }

        ticket.AnswerByBot(answer.Text, answer.Sources, clock.Now);
        await uow.SaveChangesAsync(ct);
        await chat.NotifyBotAnswerAsync(ticket.Id, answer.Text, answer.Sources, ct);
        return new ProcessResult.Answered(answer);
    }
}
