using TenderHack.Application.Ports;
using TenderHack.Domain.Moderation;

namespace TenderHack.Infrastructure.Moderation;

/// <summary>
/// Adapts the framework-free <see cref="ProfanityMatcher"/> (Domain) to the <see cref="IModerationRuleEngine"/>
/// port. Lives in Infrastructure — not Domain — purely because Domain cannot reference the
/// Application-owned port interface it implements; there is no I/O here.
/// </summary>
public sealed class DeterministicModerationRuleEngine : IModerationRuleEngine
{
    public ModerationRuleMatch? Evaluate(string text)
    {
        var match = ProfanityMatcher.Evaluate(text);
        return match is null
            ? null
            : new ModerationRuleMatch(match.RuleId, match.RuleVersion, match.MatchedTerm, match.Start, match.End, match.RequiresContextCheck);
    }
}
