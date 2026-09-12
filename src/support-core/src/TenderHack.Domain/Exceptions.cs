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

/// <summary>One feedback per case (web-api-v0.md §9.3) — a second submit is rejected, not overwritten.</summary>
public sealed class FeedbackAlreadySubmittedException(CaseId caseId)
    : InvalidOperationException($"Case {caseId} already has feedback.")
{
    public CaseId CaseId { get; } = caseId;
}

/// <summary>The feedback widget only ever appears after completion (web-api-v0.md §9.3).</summary>
public sealed class CaseNotCompletedException(CaseId caseId)
    : InvalidOperationException($"Case {caseId} is not completed yet.")
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

/// <summary>One handoff per case (web-api-v0.md §8) — a second `prepare` cannot bypass the existing one.</summary>
public sealed class HandoffAlreadyExistsException(CaseId caseId)
    : InvalidOperationException($"Case {caseId} already has a handoff.")
{
    public CaseId CaseId { get; } = caseId;
}

/// <summary>Confirm/retry/status commands require a prior `prepare` — there is nothing to act on yet.</summary>
public sealed class HandoffNotFoundException(CaseId caseId)
    : InvalidOperationException($"Case {caseId} has no handoff.")
{
    public CaseId CaseId { get; } = caseId;
}

/// <summary>Status facts (stage/specialist/terminal) are only accepted for an adapter-acknowledged handoff.</summary>
public sealed class HandoffNotAcceptedException(HandoffId handoffId, HandoffStatus status)
    : InvalidOperationException($"Handoff {handoffId} is {status}; status facts require an accepted handoff.")
{
    public HandoffId HandoffId { get; } = handoffId;
    public HandoffStatus Status { get; } = status;
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
