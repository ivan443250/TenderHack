using TenderHack.Domain;
using TenderHack.Domain.Feedback;
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

    public DateTimeOffset CreatedAt { get; }

    public ConversationStatus ConversationStatus { get; private set; } = ConversationStatus.Active;

    public ResolutionStatus ResolutionStatus { get; private set; } = ResolutionStatus.Unknown;

    public int ModerationWarningCount { get; private set; }

    public CompletionReason? CompletionReason { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Handoff? Handoff { get; private set; }

    public FeedbackId? FeedbackId { get; private set; }

    /// <summary>E1: soft-hide timestamp (docs/plans/active/2026-09-demo-readiness.md). Hiding never
    /// deletes case_events/turns/feedback — it only removes the case from the owner's lists.</summary>
    public DateTimeOffset? HiddenAt { get; private set; }

    /// <summary>Continuity across turns within one still-open clarification scenario (product-spec.md §7, §11).</summary>
    public TurnContext TurnContext { get; private set; } = TurnContext.Empty;

    /// <summary>Third consecutive `CLARIFY` in the same scenario becomes `HANDOFF_OFFER` instead (product-spec.md §11: "не более двух последовательных clarifications").</summary>
    public const int MaxConsecutiveClarifications = 2;

    public bool ClarificationLimitReached => TurnContext.ConsecutiveClarifications >= MaxConsecutiveClarifications;

    /// <summary>
    /// Always in revision order. The backing list is populated by persistence in whatever order the
    /// store returns rows (EF Core does not order owned collections), so position in `_turns` is
    /// never authoritative — only <see cref="Turn.Revision"/> is.
    /// </summary>
    public IReadOnlyList<Turn> Turns => [.. _turns.OrderBy(t => t.Revision)];

    /// <summary>The highest revision — the only turn that may still publish an authoritative decision.</summary>
    public Turn? ActiveTurn => _turns.Count == 0 ? null : _turns.MaxBy(t => t.Revision);

    /// <summary>Most recent thing that happened on this case — for list/sort views, not a persisted column.</summary>
    public DateTimeOffset LastActivityAt
    {
        get
        {
            var latest = CreatedAt;
            if (ActiveTurn is { } active && active.CreatedAt > latest) latest = active.CreatedAt;
            if (CompletedAt is { } completedAt && completedAt > latest) latest = completedAt;
            return latest;
        }
    }

    public Case(CaseId id, string ownerId, DateTimeOffset createdAt)
    {
        Id = id;
        OwnerId = ownerId;
        CreatedAt = createdAt;
    }

    /// <summary>Persists a new user revision. A running/queued prior turn is superseded, never deleted.</summary>
    public Turn StartTurn(DateTimeOffset now)
    {
        if (ConversationStatus != ConversationStatus.Active)
        {
            throw new CaseClosedException(Id);
        }

        var previous = ActiveTurn;
        previous?.Supersede();

        // Revision numbers are unique per case (enforced by persistence as well) — a second request
        // that started from the same snapshot computes the same number and fails to commit.
        var turn = new Turn(TurnId.New(), (previous?.Revision ?? 0) + 1, now);
        _turns.Add(turn);
        return turn;
    }

    /// <summary>
    /// Publishes a decision for the given turn/revision. Returns false without changing state when the
    /// turn has since been superseded or already reached a terminal status — a stale turn can never
    /// publish an authoritative answer, and a finished one is never rewritten.
    /// </summary>
    public bool TryPublishDecision(TurnId turnId, int revision, Decision decision)
    {
        var current = FindOpenActiveTurn(turnId, revision);
        if (current is null)
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
        var current = FindOpenActiveTurn(turnId, revision);
        if (current is null)
        {
            return false;
        }

        // Deliberately does not touch Handoff: a failed AI turn must not mutate a prior accepted handoff.
        current.Fail();
        return true;
    }

    private Turn? FindOpenActiveTurn(TurnId turnId, int revision)
    {
        var current = ActiveTurn;
        return current is { Status: TurnStatus.Queued or TurnStatus.Running }
            && current.Id == turnId
            && current.Revision == revision
            ? current
            : null;
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
        var wasActive = ConversationStatus == ConversationStatus.Active;
        if (wasActive)
        {
            ConversationStatus = ConversationStatus.ClosedSupport;
            CompletionReason = Cases.CompletionReason.Support;
            CompletedAt = now;
        }

        // CANCELLED carries no resolution fact of its own (support-adapter-v0.md §6.3: "ResolutionStatus
        // unchanged (UNKNOWN unless user set it)") — it must never overwrite whatever the user/support
        // side already established, including leaving a freshly-active case at UNKNOWN.
        if (terminal == HandoffTerminalOutcome.Cancelled)
        {
            return;
        }

        var resolved = terminal switch
        {
            HandoffTerminalOutcome.Resolved => ResolutionStatus.Resolved,
            HandoffTerminalOutcome.ClosedUnresolved => ResolutionStatus.Unresolved,
            _ => throw new ArgumentOutOfRangeException(nameof(terminal), terminal, null),
        };

        // A case already closed (by the user or an earlier support fact) keeps its resolution; a
        // later terminal fact may only fill in an UNKNOWN, never overwrite an explicit answer
        // (support-adapter-v0.md §6.3: "updates ... ResolutionStatus only if it was UNKNOWN").
        if (wasActive || ResolutionStatus == ResolutionStatus.Unknown)
        {
            ResolutionStatus = resolved;
        }
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

    /// <summary>
    /// One feedback per case, only after completion — the widget appears on `FEEDBACK_REQUESTED`
    /// or on reload when `completed_at != null &amp;&amp; feedback == null` (web-api-v0.md §9.3). The
    /// `solved` signal (never rating signals) may still move resolution from `UNKNOWN`.
    /// </summary>
    public void RecordFeedback(FeedbackId feedbackId, bool? solved)
    {
        if (CompletedAt is null)
        {
            throw new CaseNotCompletedException(Id);
        }

        if (FeedbackId is not null)
        {
            throw new FeedbackAlreadySubmittedException(Id);
        }

        FeedbackId = feedbackId;

        if (solved is { } value)
        {
            ApplyFeedbackSolvedSignal(value);
        }
    }

    public Handoff PrepareHandoff(HandoffPackage package)
    {
        if (ConversationStatus != ConversationStatus.Active)
        {
            throw new CaseClosedException(Id);
        }

        if (Handoff is not null)
        {
            throw new HandoffAlreadyExistsException(Id);
        }

        Handoff = new Handoff(HandoffId.New(), Id, package);
        return Handoff;
    }

    public void ConfirmHandoff(string summary)
    {
        RequireHandoff().Confirm(summary);
    }

    public void AcknowledgeHandoff(bool simulated, string? externalCaseId, DateTimeOffset now)
    {
        RequireHandoff().Acknowledge(simulated, externalCaseId, now);
    }

    public void FailHandoff()
    {
        RequireHandoff().MarkFailed();
    }

    public void RetryHandoff(string summary)
    {
        RequireHandoff().Retry(summary);
    }

    /// <summary>Status polling reached TTL without a terminal fact — UI shows «статус не обновляется».</summary>
    public void MarkHandoffStale()
    {
        RequireHandoff().MarkStale();
    }

    /// <summary>Single write path for status facts, whichever channel (poll or webhook) delivered them.</summary>
    /// <returns>false when the update duplicated an already-applied external revision and was ignored.</returns>
    public bool IngestHandoffStatus(
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
            return false;
        }

        if (terminal is { } outcome)
        {
            CompleteBySupport(outcome, now);
        }

        return true;
    }

    /// <summary>Merges freshly-understood slots into known continuity for this case (product-spec.md §7: "не спрашивать уже известное").</summary>
    public void ObserveUnderstanding(string questionText, IEnumerable<ContextSlot> slots) =>
        TurnContext = TurnContext.WithObservedQuestion(questionText, slots);

    /// <summary>Records that this turn asked a clarifying question — advances the consecutive-clarification counter.</summary>
    public void RecordClarification(IReadOnlyList<string> missingConditions) =>
        TurnContext = TurnContext.WithClarification(missingConditions);

    /// <summary>The clarification scenario is over (an actual `ANSWER`/`HANDOFF_OFFER`/`ANSWER_AND_HANDOFF` was published) — a new one starts clean.</summary>
    public void ResetClarificationLoop() =>
        TurnContext = TurnContext.WithClarificationLoopReset();

    /// <summary>
    /// If a clarification is currently outstanding, records the user's "не знаю" against it so it is
    /// never asked again in this case (product-spec.md §11) and returns the declined conditions for
    /// the caller to route to handoff with. Returns false (nothing declined) when there was no
    /// pending clarification to decline.
    /// </summary>
    public bool TryDeclineCurrentClarification(out IReadOnlyList<string> declinedConditions)
    {
        if (!TurnContext.HasPendingClarification)
        {
            declinedConditions = [];
            return false;
        }

        declinedConditions = TurnContext.LastMissingConditions;
        TurnContext = TurnContext.WithDeclinedCurrentClarification();
        return true;
    }

    private Handoff RequireHandoff() =>
        Handoff ?? throw new HandoffNotFoundException(Id);

    /// <summary>
    /// E1 (docs/plans/active/2026-09-demo-readiness.md): soft-hide, not delete — `case_events`,
    /// `turns`, `feedback` and the knowledge quality corpus are untouched (AGENTS.md §3: the analytics
    /// trail and "history is readable" are invariants). Idempotent: a second call is a no-op. A case
    /// with a live handoff (requested but not yet terminal) cannot be hidden — the specialist has a
    /// real open request and its status/notifications must still reach the user. An active
    /// conversation is completed first through the existing user-completion path, so there is still
    /// exactly one way a case becomes `CLOSED_USER` and exactly one `CASE_COMPLETED` for it.
    /// </summary>
    public void Hide(DateTimeOffset now)
    {
        if (HiddenAt is not null)
        {
            return;
        }

        if (Handoff is { Terminal: null } handoff && handoff.Status is HandoffStatus.Pending or HandoffStatus.Accepted or HandoffStatus.SimulatedAccepted)
        {
            throw new HandoffInProgressException(Id);
        }

        if (ConversationStatus == ConversationStatus.Active)
        {
            CompleteByUser(solved: null, now);
        }

        HiddenAt = now;
    }
}
