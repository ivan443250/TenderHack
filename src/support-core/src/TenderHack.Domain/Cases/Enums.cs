namespace TenderHack.Domain.Cases;

// architecture.md §6 — keep these dimensions orthogonal; do not collapse them into one enum.

public enum ConversationStatus
{
    Active,
    ClosedUser,
    ClosedSupport,
    ClosedModeration,
}

public enum ResolutionStatus
{
    Unknown,
    Resolved,
    Unresolved,
}

public enum TurnStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Superseded,
}

public enum Decision
{
    Answer,
    Clarify,
    HandoffOffer,
    AnswerAndHandoff,
    OutOfScope,
    ModerationWarning,
    ModerationClose,
    TechnicalError,
}

public enum CompletionReason
{
    User,
    Support,
    Moderation,
}
