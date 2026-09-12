namespace TenderHack.Application.Ports;

/// <summary>
/// Deterministic-first profanity check (architecture.md §5.4). Synchronous and side-effect-free by
/// design: no I/O, no model call — the ambiguous case is escalated separately to
/// `knowledge.AssessModerationContextAsync`, never decided here.
/// </summary>
public interface IModerationRuleEngine
{
    ModerationRuleMatch? Evaluate(string text);
}

/// <summary>
/// A rule hit. When <see cref="RequiresContextCheck"/> is true the match is ambiguous and the
/// orchestrator must escalate to `knowledge.AssessModerationContextAsync` before treating it as a
/// confirmed violation (product-spec.md §14 step 4).
/// </summary>
public sealed record ModerationRuleMatch(string RuleId, string RuleVersion, string MatchedTerm, int Start, int End, bool RequiresContextCheck);
