using System.Globalization;
using System.Text;

namespace TenderHack.Domain.Moderation;

/// <summary>
/// Deterministic text normalization before rule matching (product-spec.md §14 pipeline step 1-3):
/// lowercases, collapses whitespace, and undoes the cheapest obfuscation tricks (leet-speak digit
/// substitution, repeated-letter stretching, stray punctuation between letters of one word).
/// </summary>
public static class ModerationNormalizer
{
    private static readonly Dictionary<char, char> LeetSpeak = new()
    {
        ['0'] = 'о',
        ['1'] = 'i',
        ['3'] = 'e',
        ['4'] = 'a',
        ['@'] = 'a',
        ['$'] = 's',
    };

    public static string Normalize(string text)
    {
        var lowered = text.ToLower(CultureInfo.InvariantCulture);

        var builder = new StringBuilder(lowered.Length);
        char? previousLetter = null;

        foreach (var ch in lowered)
        {
            var mapped = LeetSpeak.GetValueOrDefault(ch, ch);

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
