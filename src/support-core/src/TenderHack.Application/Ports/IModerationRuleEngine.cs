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

public sealed record ModerationRuleMatch(bool Confirmed, string RuleVersion, IReadOnlyList<string> MatchedTerms);
