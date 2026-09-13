using System.Text.RegularExpressions;

namespace TenderHack.Domain.Routing;

/// <summary>
/// Small deterministic guard for messages that are plainly conversational or unrelated to the
/// supplier portal. It is deliberately conservative: an unrecognised message remains in the
/// normal knowledge pipeline, while only obvious greetings, thanks, social questions and clearly
/// foreign topics are short-circuited without an LLM call or a support handoff.
/// </summary>
public static partial class SupportScopeDetector
{
    private const string Greeting =
        "Здравствуйте! Я помогу разобраться с работой Портала поставщиков. Что у вас произошло?";
    private const string Thanks =
        "Пожалуйста! Если появится вопрос по Порталу поставщиков, я помогу.";
    private const string ScopeGuidance =
        "Я помогаю с вопросами по работе Портала поставщиков. Спросите меня о СТЕ, офертах, закупках, МЧД, электронных документах или личном кабинете.";

    [GeneratedRegex(@"^\s*(?:привет|здравствуйте|здрасте|добрый\s+(?:день|вечер)|доброе\s+утро|хай|hello|hi)[!,.\s]*$", RegexOptions.IgnoreCase)]
    private static partial Regex GreetingRegex();

    [GeneratedRegex(@"^\s*(?:спасибо|благодарю|спс)[!,.\s]*$", RegexOptions.IgnoreCase)]
    private static partial Regex ThanksRegex();

    [GeneratedRegex(@"\b(?:как\s+дела|кто\s+ты|какая\s+сегодня\s+погода|что\s+ты\s+умеешь)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SocialRegex();

    [GeneratedRegex(@"\b(?:портал|поставщик\w*|заказчик\w*|ст[еи]\b|оферт\w*|закупк\w*|мчд\b|упд\b|укд\b|yml\b|эдо\b|документ\w*|каталог\w*|личн\w*\s+кабинет\w*|рдик\b|ошибк\w*|статус\w*|импорт\w*|загруз\w*|подпис\w*|исполнен\w*)", RegexOptions.IgnoreCase)]
    private static partial Regex PortalTopicRegex();

    [GeneratedRegex(@"^\s*как\s+приготовить\b", RegexOptions.IgnoreCase)]
    private static partial Regex CookingIntentRegex();

    [GeneratedRegex(@"^\s*(?:как\s+(?:написать|настроить|подключить)|расскажи\s+про)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForeignIntentRegex();

    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9-]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex LatinTokenRegex();

    private static readonly HashSet<string> KnownPortalTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ste", "yml", "upd", "ukd", "mcd", "edo", "api",
    };

    /// <summary>Returns a server-authored user-facing reply when the message is clearly outside scope.</summary>
    public static bool TryGetReply(string text, out string reply)
    {
        var normalized = text.Trim();

        if (GreetingRegex().IsMatch(normalized))
        {
            reply = Greeting;
            return true;
        }

        if (ThanksRegex().IsMatch(normalized))
        {
            reply = Thanks;
            return true;
        }

        if (SocialRegex().IsMatch(normalized))
        {
            reply = ScopeGuidance;
            return true;
        }

        if ((CookingIntentRegex().IsMatch(normalized) && !PortalTopicRegex().IsMatch(normalized)) ||
            (ForeignIntentRegex().IsMatch(normalized) && HasUnknownLatinTopic(normalized)))
        {
            reply = ScopeGuidance;
            return true;
        }

        reply = string.Empty;
        return false;
    }

    private static bool HasUnknownLatinTopic(string text) =>
        LatinTokenRegex().Matches(text).Cast<Match>().Any(match => !KnownPortalTokens.Contains(match.Value));
}
