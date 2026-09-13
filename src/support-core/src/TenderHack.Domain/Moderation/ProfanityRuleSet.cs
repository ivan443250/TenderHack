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

    [GeneratedRegex(@"\bбля[а-яё]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex BlyaRegex();

    /// <summary>
    /// Allows a common verb prefix ("на-", "по-", "за-", "от-", "вы-", "о-", "до-", "пере-") glued
    /// directly onto the root with no internal word boundary — e.g. "нахуй", "охуеть" (quality.md §6:
    /// prefixed forms were previously missed because \b sits between prefix and root, not before it).
    /// </summary>
    [GeneratedRegex(@"\b(?:на|по|за|от|вы|о|до|пере)?ху[йеяю][а-яё]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex HuyRegex();

    [GeneratedRegex(@"\bпизд[а-яё]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex PizdRegex();

    /// <summary>
    /// Matches both "е" and "ё" spellings of the root ("ебать"/"ёбаный") with the same optional glued
    /// prefix as <see cref="HuyRegex"/> (covers "заебал", "ёбаный"), plus an optional hard sign between
    /// a prefix and the root ("отъебись") — the trailing suffix is unbounded like the sibling rules
    /// above (quality.md §6: a `{1,6}` cap previously missed long inflected forms such as
    /// "выебываться"). Excludes "ебитда" (the finance term), which is not a form of this root
    /// (quality.md §6 "substrings inside benign words" regression, same pattern as <see cref="DuraRegex"/>).
    /// </summary>
    [GeneratedRegex(@"\b(?:(?:на|по|за|от|вы|о|до|пере)ъ?)?[её]б(?!итда)[а-яё]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex EbatRegex();

    [GeneratedRegex(@"\bсук[аи][а-яё]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex SukaRegex();

    /// <summary>
    /// Excludes "дурман"/"дурманящий" — an unrelated dictionary word ("herb"/"stupor"), not a form
    /// of "дура" (quality.md §6 "substrings inside benign words" regression).
    /// </summary>
    [GeneratedRegex(@"\bдур(?!ман)[а-яё]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex DuraRegex();

    [GeneratedRegex(@"\bидиот[а-яё]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex IdiotRegex();

    /// <summary>Excludes "тупик"/"тупиковый" ("dead end") — an unrelated word, not a form of "тупой" (quality.md §6).</summary>
    [GeneratedRegex(@"\bтуп(?!ик)[а-яё]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex TupoyRegex();
}

/// <summary>One deterministic pattern within a <see cref="ProfanityRuleSet"/>.</summary>
public sealed record ProfanityRule(string Id, Regex Pattern);
