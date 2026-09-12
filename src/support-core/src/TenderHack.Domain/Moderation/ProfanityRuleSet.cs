using System.Text.RegularExpressions;

namespace TenderHack.Domain.Moderation;

/// <summary>
/// Versioned deterministic rule set (product-spec.md §14 pipeline step 2, 5: "словарь/формы/word
/// boundaries", "хранить rule_id, version и source offset"). This is an engineering-owned starting
/// baseline, not the final moderation policy — the actual word list and severity split is a
/// product/QA content decision (workstreams.md §3: "content of domain instructions / truth policy"
/// must not be decided by backend alone) and should be replaced/extended from real reviewed cases.
/// </summary>
public static partial class ProfanityRuleSet
{
    public const string Version = "v1";

    /// <summary>Unambiguous — a hit here is a confirmed violation with no context check.</summary>
    public static readonly IReadOnlyList<ProfanityRule> ConfirmedRules =
    [
        new("PROFANITY_001", BlyaRegex()),
        new("PROFANITY_002", HuyRegex()),
        new("PROFANITY_003", PizdRegex()),
        new("PROFANITY_004", EbatRegex()),
        new("PROFANITY_005", SukaRegex()),
    ];

    /// <summary>Borderline insults that are often non-offensive in support context ("тупой баг",
    /// "дурацкая форма") — flagged for `knowledge.moderation_context` rather than auto-confirmed.</summary>
    public static readonly IReadOnlyList<ProfanityRule> AmbiguousRules =
    [
        new("PROFANITY_A01", DuraRegex()),
        new("PROFANITY_A02", IdiotRegex()),
        new("PROFANITY_A03", TupoyRegex()),
    ];

    [GeneratedRegex(@"\bбля[а-я]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex BlyaRegex();

    [GeneratedRegex(@"\bху[йеяю][а-я]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex HuyRegex();

    [GeneratedRegex(@"\bпизд[а-я]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex PizdRegex();

    [GeneratedRegex(@"\bеб[а-я]{1,6}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EbatRegex();

    [GeneratedRegex(@"\bсук[аи][а-я]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex SukaRegex();

    [GeneratedRegex(@"\bдур[а-я]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex DuraRegex();

    [GeneratedRegex(@"\bидиот[а-я]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex IdiotRegex();

    [GeneratedRegex(@"\bтуп[а-я]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex TupoyRegex();
}

/// <summary>One deterministic pattern within a <see cref="ProfanityRuleSet"/>.</summary>
public sealed record ProfanityRule(string Id, Regex Pattern);
