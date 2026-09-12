namespace TenderHack.Application.Knowledge;

/// <summary>
/// Raised by <see cref="IKnowledgeService"/> implementations for any failed stage call
/// (timeout, unavailable, invalid response, model error). <see cref="Orchestration.TurnOrchestrator"/>
/// catches this and produces `TECHNICAL_ERROR` — it must never be reinterpreted as "no information"
/// (architecture.md §16).
/// </summary>
public sealed class KnowledgeFailureException(KnowledgeFailureCategory category, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public KnowledgeFailureCategory Category { get; } = category;
}
