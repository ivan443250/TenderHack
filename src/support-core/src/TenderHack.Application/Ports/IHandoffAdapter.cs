using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Application.Ports;

/// <summary>
/// Port for the frozen `support-adapter-v0` boundary (architecture.md §15). Not yet called by any
/// use-case in this stage — `HandoffPrepare`/`Confirm`/status-sync land in a later phase — but
/// defined now because it is one of the six ports `Application` owns (architecture.md §4.1).
/// </summary>
public interface IHandoffAdapter
{
    Task<HandoffAck> SubmitAsync(HandoffRequest request, string idempotencyKey, CancellationToken ct);

    /// <summary>Null when the adapter has no status API for this handoff.</summary>
    Task<HandoffStatusSnapshot?> GetStatusAsync(HandoffStatusQuery query, CancellationToken ct);
}

public sealed record HandoffRequest(
    CaseId CaseId,
    HandoffId HandoffId,
    string Summary,
    IReadOnlyList<string> EvidenceFragmentIds);

public sealed record HandoffAck(bool Accepted, bool Simulated, string? ExternalCaseId);

public sealed record HandoffStatusQuery(CaseId CaseId, HandoffId HandoffId, string? ExternalCaseId);

public sealed record HandoffStatusSnapshot(
    string ExternalCaseId,
    HandoffStage? Stage,
    AssignedSpecialist? AssignedSpecialist,
    HandoffTerminalOutcome? Terminal,
    long ExternalRevision,
    DateTimeOffset OccurredAt,
    IntegrationMode IntegrationMode);
