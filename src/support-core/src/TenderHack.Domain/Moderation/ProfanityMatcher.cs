namespace TenderHack.Domain.Moderation;

/// <summary>
/// Applies <see cref="ModerationNormalizer"/> then <see cref="ProfanityRuleSet"/> to one message.
/// Confirmed-list hits never need a context check; ambiguous-list hits do (product-spec.md §14 step 4).
/// </summary>
public static class ProfanityMatcher
{
    public static ModerationMatch? Evaluate(string text)
    {
        var normalized = ModerationNormalizer.Normalize(text);

        foreach (var rule in ProfanityRuleSet.ConfirmedRules)
        {
            var match = rule.Pattern.Match(normalized);
            if (match.Success)
            {
                return new ModerationMatch(rule.Id, ProfanityRuleSet.Version, match.Value, match.Index, match.Index + match.Length, RequiresContextCheck: false);
            }
        }

        foreach (var rule in ProfanityRuleSet.AmbiguousRules)
        {
            var match = rule.Pattern.Match(normalized);
            if (match.Success)
            {
                return new ModerationMatch(rule.Id, ProfanityRuleSet.Version, match.Value, match.Index, match.Index + match.Length, RequiresContextCheck: true);
            }
        }

        return null;
    }
}

/// <summary>
/// <see cref="Start"/>/<see cref="End"/> are offsets into the normalized text, not the raw user
/// message — normalization can drop/collapse characters, so exact-original-offset mapping is a
/// known follow-up, not yet implemented.
/// </summary>
public sealed record ModerationMatch(string RuleId, string RuleVersion, string MatchedTerm, int Start, int End, bool RequiresContextCheck);
