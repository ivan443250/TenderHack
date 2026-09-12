using TenderHack.Application.Ports;

namespace TenderHack.Api.Moderation;

/// <summary>
/// Temporary stand-in for the real deterministic profanity engine (architecture.md §5.4). Truthfully
/// has zero rules configured — it never confirms a violation — until a later stage replaces it with
/// the versioned normalizer/rule set. Never claim it does content moderation; it does not.
/// </summary>
public sealed class PassthroughModerationRuleEngine : IModerationRuleEngine
{
    public ModerationRuleMatch? Evaluate(string text) => null;
}
