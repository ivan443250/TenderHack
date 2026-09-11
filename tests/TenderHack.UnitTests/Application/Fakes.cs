using TenderHack.Application.Abstractions;
using TenderHack.Domain.Entities;
using TenderHack.Domain.Enums;
using TenderHack.Domain.ValueObjects;

namespace TenderHack.UnitTests.Application;

internal sealed class FakeTicketRepository : ITicketRepository
{
    public Ticket? Current { get; private set; }

    public Task<Ticket> GetOrCreateAsync(Guid userId, CancellationToken ct)
    {
        Current ??= Ticket.Create(Guid.NewGuid(), userId, DateTimeOffset.UtcNow);
        return Task.FromResult(Current);
    }

    public Task<Ticket?> GetWithMessagesAsync(Guid ticketId, CancellationToken ct) => Task.FromResult(Current);

    public Task<IReadOnlyList<Ticket>> GetQueueAsync(SupportLine line, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Ticket>>([]);

    public Task<Specialist?> GetSpecialistAsync(Guid specialistId, CancellationToken ct) =>
        Task.FromResult<Specialist?>(null);

    public void Add(Ticket ticket) => Current = ticket;
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

internal sealed class FakeChatNotifier : IChatNotifier
{
    public bool ViolationNotified { get; private set; }
    public bool AwaitingOperatorNotified { get; private set; }
    public bool BotAnswerNotified { get; private set; }

    public Task NotifyViolationAsync(Guid ticketId, CancellationToken ct)
    {
        ViolationNotified = true;
        return Task.CompletedTask;
    }

    public Task NotifyBotAnswerAsync(Guid ticketId, string answer, IReadOnlyList<SourceRef> sources, CancellationToken ct)
    {
        BotAnswerNotified = true;
        return Task.CompletedTask;
    }

    public Task NotifyAwaitingOperatorAsync(Guid ticketId, CancellationToken ct)
    {
        AwaitingOperatorNotified = true;
        return Task.CompletedTask;
    }

    public Task NotifyOperatorMessageAsync(Guid ticketId, string text, CancellationToken ct) => Task.CompletedTask;

    public Task StreamAnswerTokenAsync(Guid ticketId, string token, CancellationToken ct) => Task.CompletedTask;

    public Task StreamStageAsync(Guid ticketId, string stage, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class FakeOperatorNotifier : IOperatorNotifier
{
    public bool QueueNotified { get; private set; }

    public Task NotifyQueueAsync(SupportLine line, Guid ticketId, CancellationToken ct)
    {
        QueueNotified = true;
        return Task.CompletedTask;
    }
}

internal sealed class FakeDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}

internal sealed class FakeThresholdProvider(double minConfidence = 0.55) : IThresholdProvider
{
    public double MinConfidence { get; } = minConfidence;
}

internal sealed class FakeMlService(TriageResult triage, AnswerResult answer) : IMlService
{
    public Task<TriageResult> TriageAsync(string text, CancellationToken ct) => Task.FromResult(triage);

    public Task<AnswerResult> AnswerAsync(string query, IReadOnlyList<string> history, CancellationToken ct) =>
        Task.FromResult(answer);

    public Task<IReadOnlyList<ProblemCluster>> ClusterFeedbackAsync(IReadOnlyList<FeedbackText> items, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProblemCluster>>([]);
}
