using System.Text.RegularExpressions;

namespace TenderHack.Domain.Routing;

/// <summary>
/// Deterministic keyword check for an explicit request to talk to a person — no LLM needed
/// (architecture.md §5.2 "direct human-request check"; product-spec.md §10 "Exact parsing, direct
/// human request и простые deterministic rules не требуют LLM").
/// </summary>
public static partial class DirectHumanRequestDetector
{
    // Fixed phrases that are unambiguous on their own — a plain substring match is fine because none
    // of them collide with ordinary Portal-support vocabulary.
    private static readonly string[] Phrases =
    [
        "живой человек",
        "живого человека",
        "с человеком",
        "техподдержк",
        "позовите специалиста",
        "соедините",
        "переключите на",
    ];

    /// <summary>
    /// `оператор` on its own is not safe as a bare substring: "оператор ЭДО" /
    /// "оператор электронного документооборота" is ordinary domain vocabulary (product-spec.md §21
    /// keeps EDO-provider terminology distinct from a support request), not a request for a person.
    /// Word-bounded, with a negative lookahead that excludes exactly that EDO-provider phrasing.
    /// </summary>
    [GeneratedRegex(@"\bоператор[а-яё]*\b(?!\s+(эдо|электронного\s+документооборота))", RegexOptions.IgnoreCase)]
    private static partial Regex OperatorRegex();

    public static bool IsExplicitRequest(string text)
    {
        var normalized = text.ToLowerInvariant();
        return Phrases.Any(normalized.Contains) || OperatorRegex().IsMatch(normalized);
    }
}
