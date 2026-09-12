namespace TenderHack.Domain.Cases;

/// <summary>
/// Provenance vocabulary for a context slot (product-spec.md §7): `trusted_portal_context` and
/// `user_explicit` are facts safe to use and to stop asking about; `inferred` is an internal
/// candidate that must never be shown as confirmed; `unknown` means "not a subject known here".
/// This mirrors <c>TenderHack.Application.Knowledge.EntityProvenance</c> in shape only — Domain
/// cannot reference an Application type, so <see cref="Application.Orchestration.TurnOrchestrator"/>
/// maps between the two at the boundary.
/// </summary>
public enum ContextSlotProvenance
{
    UserExplicit,
    TrustedPortalContext,
    Inferred,
    Unknown,
}

/// <summary>One context slot (role, process, document type, status, error code, ...) known about a case.</summary>
public sealed record ContextSlot(string Type, string Value, ContextSlotProvenance Provenance);

/// <summary>
/// Continuity across turns within one still-open clarification scenario (product-spec.md §7, §11):
/// known slots merged across turns so an already-answered question is never asked again, the
/// consecutive-clarification counter/limit, and which missing conditions the user has already
/// declined ("не знаю") so the same one is never re-asked.
///
/// Immutable by design: <see cref="Case"/> replaces its own reference to this record wholesale on
/// every observed change rather than mutating one in place, both because the record has no identity
/// of its own and because EF Core's change tracking for a converted reference-typed column needs a
/// new instance to reliably detect a change.
/// </summary>
public sealed record TurnContext(
    IReadOnlyList<ContextSlot> KnownSlots,
    int ConsecutiveClarifications,
    string? LastQuestionText,
    IReadOnlyList<string> LastMissingConditions,
    IReadOnlyList<string> DeclinedMissingConditions)
{
    public static TurnContext Empty { get; } = new([], 0, null, [], []);

    public bool HasPendingClarification => ConsecutiveClarifications > 0;

    /// <summary>
    /// Merges freshly-understood slots in. A slot already known with an equal-or-stronger
    /// provenance is kept only if the new value is itself user-explicit or trusted (a correction);
    /// a merely-inferred candidate never overwrites a stronger fact already on file.
    /// </summary>
    public TurnContext WithObservedQuestion(string questionText, IEnumerable<ContextSlot> slots)
    {
        var merged = new List<ContextSlot>(KnownSlots);
        foreach (var slot in slots)
        {
            var existingIndex = merged.FindIndex(s => s.Type == slot.Type);
            if (existingIndex < 0)
            {
                merged.Add(slot);
                continue;
            }

            if (Rank(slot.Provenance) >= Rank(merged[existingIndex].Provenance))
            {
                merged[existingIndex] = slot;
            }
        }

        return this with { KnownSlots = merged, LastQuestionText = questionText };
    }

    public TurnContext WithClarification(IReadOnlyList<string> missingConditions) =>
        this with { ConsecutiveClarifications = ConsecutiveClarifications + 1, LastMissingConditions = [.. missingConditions] };

    /// <summary>The one or more conditions we were just waiting on are recorded as declined — never asked again in this case.</summary>
    public TurnContext WithDeclinedCurrentClarification()
    {
        var declined = new List<string>(DeclinedMissingConditions);
        foreach (var condition in LastMissingConditions)
        {
            if (!declined.Contains(condition, StringComparer.OrdinalIgnoreCase))
            {
                declined.Add(condition);
            }
        }

        return this with { DeclinedMissingConditions = declined };
    }

    /// <summary>A turn actually resolved the scenario (anything other than another `CLARIFY`) — the clarification loop is over.</summary>
    public TurnContext WithClarificationLoopReset() =>
        this with { ConsecutiveClarifications = 0, LastMissingConditions = [] };

    private static int Rank(ContextSlotProvenance provenance) => provenance switch
    {
        ContextSlotProvenance.TrustedPortalContext or ContextSlotProvenance.UserExplicit => 2,
        ContextSlotProvenance.Inferred => 1,
        _ => 0,
    };
}
