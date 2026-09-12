using System.Text.Json;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Application.Handoff;

/// <summary>
/// Assembles the editable handoff package from persisted case state alone — never a fresh call to
/// `knowledge` (support-adapter-v0.md §2: "package can be built while knowledge is unavailable").
/// Deriving what a specialist needs from case history is a use-case concern, so it lives in
/// `Application`, not as wire-shaping in `TenderHack.Api`.
/// </summary>
public static class HandoffPackageBuilder
{
    public static HandoffPackage Build(Case @case, IReadOnlyList<PersistedCaseEvent> events)
    {
        var userReportedContext = @case.TurnContext.KnownSlots
            .Where(s => s.Provenance == ContextSlotProvenance.UserExplicit)
            .ToArray();
        var verifiedPortalContext = @case.TurnContext.KnownSlots
            .Where(s => s.Provenance == ContextSlotProvenance.TrustedPortalContext)
            .ToArray();
        // "already tried" is one of product-spec.md §7's context slot types; if `knowledge` never
        // populates it for this case, this is honestly empty rather than fabricated.
        var alreadyTried = @case.TurnContext.KnownSlots
            .Where(s => string.Equals(s.Type, "already_tried", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Value)
            .ToArray();
        var unknownFields = @case.TurnContext.LastMissingConditions
            .Concat(@case.TurnContext.DeclinedMissingConditions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var relevantMessageIds = events
            .Where(e => e.Type == "USER_MESSAGE")
            .Select(e => e.EventId.ToString())
            .ToArray();

        var (dispatchQueue, handoffReason, engineeringReviewSuggested) = ExtractRouting(events);
        var sourcesChecked = ExtractSourcesChecked(events);
        var draftSummary = BuildDraftSummary(@case, events, handoffReason);

        return new HandoffPackage(
            draftSummary,
            userReportedContext,
            verifiedPortalContext,
            alreadyTried,
            unknownFields,
            sourcesChecked,
            handoffReason,
            relevantMessageIds,
            HandoffPackage.PortalChatChannel,
            dispatchQueue,
            engineeringReviewSuggested);
    }

    /// <summary>
    /// Re-derives routing from the case's own last `HANDOFF_OFFER` event — the client is never
    /// trusted with this (web-api-v0.md §5.5). Absent entirely only when the user asks for a human
    /// before the system ever offered one (e.g. an explicit request answered before any handoff
    /// stage ran) — `USER_INITIATED` names that honestly instead of inventing a routing reason.
    /// </summary>
    private static (string DispatchQueue, IReadOnlyList<string> ReasonCodes, bool EngineeringReviewSuggested) ExtractRouting(IReadOnlyList<PersistedCaseEvent> events)
    {
        var last = events.LastOrDefault(e => e.Type == "HANDOFF_OFFER");
        if (last is null)
        {
            return ("l1-general", ["USER_INITIATED"], false);
        }

        using var document = JsonDocument.Parse(last.PayloadJson);
        var root = document.RootElement;
        var dispatchQueue = root.TryGetProperty("dispatch_queue", out var dq) ? dq.GetString() ?? "l1-general" : "l1-general";
        var reasonCodes = root.TryGetProperty("reason_codes", out var rc)
            ? rc.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToArray()
            : [];
        var engineeringReviewSuggested = root.TryGetProperty("engineering_review_suggested", out var ers) && ers.GetBoolean();
        return (dispatchQueue, reasonCodes.Length > 0 ? reasonCodes : ["USER_INITIATED"], engineeringReviewSuggested);
    }

    /// <summary>
    /// Whichever came last tells us what evidence the system actually looked at: a confirmed
    /// answer's sources, or — when no answer was ever confirmed — the fragment ids the failing
    /// retrieve/answerability pass considered (`NO_CONFIRMED_ANSWER.checked_fragment_ids`).
    /// </summary>
    private static IReadOnlyList<string> ExtractSourcesChecked(IReadOnlyList<PersistedCaseEvent> events)
    {
        var last = events.LastOrDefault(e => e.Type is "AI_ANSWER" or "NO_CONFIRMED_ANSWER");
        if (last is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(last.PayloadJson);
        var root = document.RootElement;

        if (last.Type == "AI_ANSWER" && root.TryGetProperty("sources", out var sources))
        {
            return
            [
                .. sources.EnumerateArray()
                    .Select(s => s.TryGetProperty("fragment_id", out var id) ? id.GetString() : null)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => id!),
            ];
        }

        if (last.Type == "NO_CONFIRMED_ANSWER" && root.TryGetProperty("checked_fragment_ids", out var checkedIds))
        {
            return [.. checkedIds.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0)];
        }

        return [];
    }

    private static string BuildDraftSummary(Case @case, IReadOnlyList<PersistedCaseEvent> events, IReadOnlyList<string> handoffReason)
    {
        var lastQuestionEvent = events.LastOrDefault(e => e.Type == "USER_MESSAGE");
        var questionText = lastQuestionEvent is not null
            ? TryGetString(lastQuestionEvent.PayloadJson, "text") ?? "(текст обращения недоступен)"
            : "(текст обращения недоступен)";

        var parts = new List<string> { $"Обращение: {questionText}" };
        if (@case.TurnContext.KnownSlots.Count > 0)
        {
            parts.Add("Известно: " + string.Join("; ", @case.TurnContext.KnownSlots.Select(s => $"{s.Type}={s.Value}")));
        }

        parts.Add("Причина передачи: " + string.Join(", ", handoffReason));
        return string.Join(" ", parts);
    }

    private static string? TryGetString(string payloadJson, string property)
    {
        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
    }
}
