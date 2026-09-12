using System.Globalization;
using System.Text;

namespace TenderHack.Domain.Moderation;

/// <summary>
/// Deterministic text normalization before rule matching (product-spec.md §14 pipeline step 1-3):
/// lowercases, collapses whitespace, and undoes the cheapest obfuscation tricks (leet-speak digit
/// substitution, Latin look-alike letters, repeated-letter stretching, stray punctuation between
/// letters of one word).
/// </summary>
public static class ModerationNormalizer
{
    /// <summary>
    /// Every substitution targets a <b>Cyrillic</b> letter: <see cref="ProfanityRuleSet"/> patterns
    /// are Cyrillic-only, so mapping "4" to Latin "a" would normalize "сук4" into a string no rule
    /// can ever match.
    /// </summary>
    private static readonly Dictionary<char, char> Substitutions = new()
    {
        // leet-speak digits/symbols
        ['0'] = 'о',
        ['1'] = 'и',
        ['3'] = 'е',
        ['4'] = 'а',
        ['@'] = 'а',
        ['$'] = 'с',
        // Latin letters that are visually identical to Cyrillic ones ("cyka", "пиздeц")
        ['a'] = 'а',
        ['c'] = 'с',
        ['e'] = 'е',
        ['k'] = 'к',
        ['o'] = 'о',
        ['p'] = 'р',
        ['x'] = 'х',
        ['y'] = 'у',
    };

    public static string Normalize(string text)
    {
        var lowered = text.ToLower(CultureInfo.InvariantCulture);

        var builder = new StringBuilder(lowered.Length);
        char? previousLetter = null;

        foreach (var ch in lowered)
        {
            var mapped = Substitutions.GetValueOrDefault(ch, ch);

            // Drop punctuation/symbols wedged between letters (e.g. "с.у.к.а"), but keep real word
            // boundaries (spaces, newlines) so matching still respects word boundaries.
            if (!char.IsLetterOrDigit(mapped) && !char.IsWhiteSpace(mapped))
            {
                continue;
            }

            if (char.IsWhiteSpace(mapped))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                previousLetter = null;
                continue;
            }

            // Collapse any run of the same letter to one ("сууука" -> "сука").
            if (mapped == previousLetter)
            {
                continue;
            }

            previousLetter = mapped;
            builder.Append(mapped);
        }

        return builder.ToString();
    }
}
