namespace TenderHack.Domain.Routing;

/// <summary>
/// Deterministic check for "I don't know" replies to an outstanding clarification — no LLM needed,
/// same spirit as <see cref="DirectHumanRequestDetector"/>. Used only while a case has a pending
/// clarification (product-spec.md §11: "не спрашивать повторно slot после «не знаю»") — outside
/// that context these phrases are just ordinary text and are not evaluated.
/// </summary>
public static class ClarificationDeclineDetector
{
    private static readonly string[] Phrases =
    [
        "не знаю",
        "незнаю",
        "не в курсе",
        "нет информации",
        "затрудняюсь ответить",
        "неизвестно",
    ];

    public static bool IsDecline(string text)
    {
        var normalized = text.ToLowerInvariant();
        return Phrases.Any(normalized.Contains);
    }
}
