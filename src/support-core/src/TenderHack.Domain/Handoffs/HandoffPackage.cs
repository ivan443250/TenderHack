using TenderHack.Domain.Cases;

namespace TenderHack.Domain.Handoffs;

/// <summary>
/// Editable prepared package (architecture.md §5.6, support-adapter-v0.md §2, product-spec.md §17):
/// everything the specialist needs, assembled from the case's own persisted state — never a fresh
/// call to `knowledge` — so it can be built even while `knowledge` is unavailable
/// (support-adapter-v0.md §2 "package can be built while knowledge is unavailable").
///
/// Immutable, same reasoning as <see cref="Cases.TurnContext"/>: `Handoff` replaces its reference to
/// this record wholesale rather than mutating one in place, which is what makes EF Core's
/// JSON-column change tracking actually notice an update.
/// </summary>
public sealed record HandoffPackage(
    string DraftSummary,
    IReadOnlyList<ContextSlot> UserReportedContext,
    IReadOnlyList<ContextSlot> VerifiedPortalContext,
    IReadOnlyList<string> AlreadyTried,
    IReadOnlyList<string> UnknownFields,
    IReadOnlyList<string> SourcesChecked,
    IReadOnlyList<string> HandoffReason,
    IReadOnlyList<string> RelevantMessageIds,
    string Channel,
    string DispatchQueue,
    bool EngineeringReviewSuggested)
{
    /// <summary>Fixed for now — this product has exactly one support channel (product-spec.md §17 lists "case/channel/dispatch queue" without defining a second one).</summary>
    public const string PortalChatChannel = "portal_chat";
}
