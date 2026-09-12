using TenderHack.Domain.Cases;

namespace TenderHack.Domain.Moderation;

/// <summary>
/// Warning-first profanity policy (product-spec.md §14, architecture.md §5.4): the first confirmed
/// violation in a case warns; a violation is closed once the case's warning count has already
/// reached the configured threshold. The threshold is server configuration, never a client input.
/// </summary>
public static class ModerationPolicy
{
    public static (Decision Decision, int NewWarningCount) Evaluate(int currentWarningCount, int closeAfterWarnings)
    {
        var decision = currentWarningCount >= closeAfterWarnings
            ? Decision.ModerationClose
            : Decision.ModerationWarning;

        return (decision, currentWarningCount + 1);
    }
}
