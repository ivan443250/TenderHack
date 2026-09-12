using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Application.Ports;

/// <summary>Port for the frozen `support-adapter-v0` boundary (architecture.md §15, support-adapter-v0.md).</summary>
public interface IHandoffAdapter
{
    Task<HandoffAck> SubmitAsync(HandoffRequest request, string idempotencyKey, CancellationToken ct);

    /// <summary>Null when the adapter has no status API for this handoff, or has nothing new to report this poll.</summary>
    Task<HandoffStatusSnapshot?> GetStatusAsync(HandoffStatusQuery query, CancellationToken ct);
}

/// <summary>
/// support-adapter-v0.md §2. Fields the adapter cannot know (verified Portal state, real SLA/contact)
/// are simply absent — "unknown stays unknown", never fabricated. `Summary` is the user's final
/// edited text; every other field comes straight from the case's own persisted
/// <see cref="Domain.Handoffs.HandoffPackage"/> (`HandoffPackageBuilder`), assembled without any
/// `knowledge` call so the package — and this request — can be built while `knowledge` is down.
/// </summary>
public sealed record HandoffRequest(
    CaseId CaseId,
    HandoffId HandoffId,
    string Summary,
    string Channel,
    string DispatchQueue,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ContextSlot> UserReportedContext,
    IReadOnlyList<ContextSlot> VerifiedPortalContext,
    IReadOnlyList<string> AlreadyTried,
    IReadOnlyList<string> UnknownFields,
    IReadOnlyList<string> SourcesChecked,
    IReadOnlyList<string> RelevantMessageIds,
    bool EngineeringReviewSuggested);

public sealed record HandoffAck(bool Accepted, bool Simulated, string? ExternalCaseId, string? SafeMessage = null);

public sealed record HandoffStatusQuery(
    CaseId CaseId,
    HandoffId HandoffId,
    string? ExternalCaseId,
    long? LastKnownExternalRevision,
    DateTimeOffset AcceptedAt);

public sealed record HandoffStatusSnapshot(
    string ExternalCaseId,
    HandoffStage? Stage,
    AssignedSpecialist? AssignedSpecialist,
    HandoffTerminalOutcome? Terminal,
    long ExternalRevision,
    DateTimeOffset OccurredAt,
    IntegrationMode IntegrationMode);
