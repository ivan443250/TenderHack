using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Domain;

/// <summary>A completed case rejects new user messages (architecture.md §6, web-api-v0 `CASE_CLOSED`).</summary>
public sealed class CaseClosedException(CaseId caseId)
    : InvalidOperationException($"Case {caseId} is closed and rejects new activity.")
{
    public CaseId CaseId { get; } = caseId;
}

public sealed class CaseAlreadyCompletedException(CaseId caseId)
    : InvalidOperationException($"Case {caseId} is already completed.")
{
    public CaseId CaseId { get; } = caseId;
}

public sealed class InvalidHandoffTransitionException(HandoffId handoffId, HandoffStatus from, HandoffStatus to)
    : InvalidOperationException($"Handoff {handoffId} cannot transition from {from} to {to}.")
{
    public HandoffId HandoffId { get; } = handoffId;
    public HandoffStatus From { get; } = from;
    public HandoffStatus To { get; } = to;
}

/// <summary>A status update cannot target a different handoff/case than it was issued for (architecture.md §6).</summary>
public sealed class HandoffMismatchException(
    HandoffId expectedHandoffId,
    HandoffId actualHandoffId,
    CaseId expectedCaseId,
    CaseId actualCaseId)
    : InvalidOperationException(
        $"Status update for handoff {actualHandoffId}/case {actualCaseId} does not match " +
        $"handoff {expectedHandoffId}/case {expectedCaseId}.")
{
    public HandoffId ExpectedHandoffId { get; } = expectedHandoffId;
    public HandoffId ActualHandoffId { get; } = actualHandoffId;
    public CaseId ExpectedCaseId { get; } = expectedCaseId;
    public CaseId ActualCaseId { get; } = actualCaseId;
}
