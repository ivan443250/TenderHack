using TenderHack.Domain;
using TenderHack.Domain.Handoffs;
using TenderHack.Domain.Moderation;

namespace TenderHack.Domain.Cases;

/// <summary>
/// Aggregate root for one support conversation. State transitions are explicit methods with
/// preconditions (architecture.md §5.1) — never arbitrary field mutation from outside.
/// </summary>
public sealed class Case
{
    private readonly List<Turn> _turns = [];

    public CaseId Id { get; }

    public string OwnerId { get; }

    public ConversationStatus ConversationStatus { get; private set; } = ConversationStatus.Active;

    public ResolutionStatus ResolutionStatus { get; private set; } = ResolutionStatus.Unknown;

    public int ModerationWarningCount { get; private set; }

    public CompletionReason? CompletionReason { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Handoff? Handoff { get; private set; }

    public IReadOnlyList<Turn> Turns => _turns;

    public Turn? ActiveTurn => _turns.Count == 0 ? null : _turns[^1];

    public Case(CaseId id, string ownerId)
    {
        Id = id;
        OwnerId = ownerId;
    }

    /// <summary>Persists a new user revision. A running/queued prior turn is superseded, never deleted.</summary>
    public Turn StartTurn(DateTimeOffset now)
    {
        if (ConversationStatus != ConversationStatus.Active)
        {
            throw new CaseClosedException(Id);
        }

        ActiveTurn?.Supersede();

        var turn = new Turn(TurnId.New(), _turns.Count + 1, now);
        _turns.Add(turn);
        return turn;
    }

    /// <summary>
    /// Publishes a decision for the given turn/revision. Returns false without changing state when the
    /// turn has since been superseded — a stale turn can never publish an authoritative answer.
    /// </summary>
    public bool TryPublishDecision(TurnId turnId, int revision, Decision decision)
    {
        var current = ActiveTurn;
        if (current is null || current.Id != turnId || current.Revision != revision
            || current.Status == TurnStatus.Superseded)
        {
            return false;
        }

        // ANSWER / ANSWER_AND_HANDOFF intentionally never touch ResolutionStatus here.
        current.Complete(decision);
        return true;
    }

    /// <summary>Same staleness rule as <see cref="TryPublishDecision"/>, for a failed AI turn.</summary>
    public bool TryFailTurn(TurnId turnId, int revision)
    {
        var current = ActiveTurn;
        if (current is null || current.Id != turnId || current.Revision != revision
            || current.Status == TurnStatus.Superseded)
        {
            return false;
        }

        // Deliberately does not touch Handoff: a failed AI turn must not mutate a prior accepted handoff.
        current.Fail();
        return true;
    }

    /// <summary>Applies one confirmed profanity violation under the warning-first policy.</summary>
    public Decision RecordModerationViolation(int closeAfterWarnings, DateTimeOffset now)
    {
        if (ConversationStatus != ConversationStatus.Active)
        {
            throw new CaseClosedException(Id);
        }

        var (decision, newCount) = ModerationPolicy.Evaluate(ModerationWarningCount, closeAfterWarnings);
        ModerationWarningCount = newCount;

        // Deliberately does not touch Handoff: moderation close must not revoke an accepted handoff.
        if (decision == Decision.ModerationClose)
        {
            ConversationStatus = ConversationStatus.ClosedModeration;
            CompletionReason = Cases.CompletionReason.Moderation;
            CompletedAt = now;
        }

        ActiveTurn?.Complete(decision);
        return decision;
    }

    public void CompleteByUser(bool? solved, DateTimeOffset now)
    {
        if (ConversationStatus != ConversationStatus.Active)
        {
            throw new CaseAlreadyCompletedException(Id);
        }

        ResolutionStatus = solved switch
        {
            true => ResolutionStatus.Resolved,
            false => ResolutionStatus.Unresolved,
            null => ResolutionStatus.Unknown,
        };
        ConversationStatus = ConversationStatus.ClosedUser;
        CompletionReason = Cases.CompletionReason.User;
        CompletedAt = now;
    }

    /// <summary>
    /// Applies an adapter terminal outcome. A case already closed by the user keeps its
    /// conversation status — only resolution/handoff facts update (architecture.md §6).
    /// </summary>
    public void CompleteBySupport(HandoffTerminalOutcome terminal, DateTimeOffset now)
    {
        if (ConversationStatus == ConversationStatus.Active)
        {
            ConversationStatus = ConversationStatus.ClosedSupport;
            CompletionReason = Cases.CompletionReason.Support;
            CompletedAt = now;
        }

        ResolutionStatus = terminal switch
        {
            HandoffTerminalOutcome.Resolved => ResolutionStatus.Resolved,
            HandoffTerminalOutcome.ClosedUnresolved => ResolutionStatus.Unresolved,
            HandoffTerminalOutcome.Cancelled => ResolutionStatus.Unresolved,
            _ => throw new ArgumentOutOfRangeException(nameof(terminal), terminal, null),
        };
    }

    /// <summary>
    /// The `solved` feedback signal (web-api-v0 §9.3) — the only feedback field that may move
    /// resolution, and only while it is still unknown. Rating signals never call this.
    /// </summary>
    public void ApplyFeedbackSolvedSignal(bool solved)
    {
        if (ResolutionStatus != ResolutionStatus.Unknown)
        {
            return;
        }

        ResolutionStatus = solved ? ResolutionStatus.Resolved : ResolutionStatus.Unresolved;
    }

    public Handoff PrepareHandoff()
    {
        if (ConversationStatus != ConversationStatus.Active)
        {
            throw new CaseClosedException(Id);
        }

        if (Handoff is not null)
        {
            throw new InvalidOperationException($"Case {Id} already has a handoff; use Retry, not a new prepare.");
        }

        Handoff = new Handoff(HandoffId.New(), Id);
        return Handoff;
    }

    public void ConfirmHandoff()
    {
        RequireHandoff().Confirm();
    }

    public void AcknowledgeHandoff(bool simulated, string? externalCaseId)
    {
        RequireHandoff().Acknowledge(simulated, externalCaseId);
    }

    public void FailHandoff()
    {
        RequireHandoff().MarkFailed();
    }

    public void RetryHandoff()
    {
        RequireHandoff().Retry();
    }

    /// <summary>Single write path for status facts, whichever channel (poll or webhook) delivered them.</summary>
    public void IngestHandoffStatus(
        HandoffId handoffId,
        long externalRevision,
        HandoffStage? stage,
        AssignedSpecialist? assignedSpecialist,
        HandoffTerminalOutcome? terminal,
        DateTimeOffset now)
    {
        var handoff = RequireHandoff();
        var applied = handoff.IngestStatus(handoffId, Id, externalRevision, stage, assignedSpecialist, terminal);
        if (!applied)
        {
            return;
        }

        if (terminal is { } outcome)
        {
            CompleteBySupport(outcome, now);
        }
    }

    private Handoff RequireHandoff() =>
        Handoff ?? throw new InvalidOperationException($"Case {Id} has no handoff.");
}
