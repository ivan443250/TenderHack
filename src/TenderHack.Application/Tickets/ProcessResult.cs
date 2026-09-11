using TenderHack.Application.Abstractions;
using TenderHack.Domain.Enums;

namespace TenderHack.Application.Tickets;

public abstract record ProcessResult
{
    public sealed record Answered(AnswerResult Answer) : ProcessResult;

    public sealed record Escalated(SupportLine Line) : ProcessResult;

    public sealed record ClosedByViolationResult : ProcessResult;

    public static ProcessResult ClosedByViolation { get; } = new ClosedByViolationResult();
}
