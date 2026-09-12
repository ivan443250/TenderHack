namespace TenderHack.Domain.Routing;

/// <summary>
/// Deterministic keyword check for an explicit request to talk to a person — no LLM needed
/// (architecture.md §5.2 "direct human-request check"; product-spec.md §10 "Exact parsing, direct
/// human request и простые deterministic rules не требуют LLM").
/// </summary>
public static class DirectHumanRequestDetector
{
    private static readonly string[] Phrases =
    [
        "оператор",
        "живой человек",
        "живого человека",
        "с человеком",
        "техподдержк",
        "позовите специалиста",
        "соедините",
        "переключите на",
    ];

    public static bool IsExplicitRequest(string text)
    {
        var normalized = text.ToLowerInvariant();
        return Phrases.Any(normalized.Contains);
    }
}
