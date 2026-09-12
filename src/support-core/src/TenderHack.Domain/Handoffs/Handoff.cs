using TenderHack.Domain;
using TenderHack.Domain.Cases;

namespace TenderHack.Domain.Handoffs;

/// <summary>
/// Editable prepared package through delivery/status tracking (architecture.md §5.6). One handoff
/// per case: acceptance only follows adapter acknowledgement, and status facts only ever arrive
/// through <see cref="IngestStatus"/> — nothing here invents a specialist or stage.
/// </summary>
public sealed class Handoff
{
    public HandoffId Id { get; }

    public CaseId CaseId { get; }

    public HandoffStatus Status { get; private set; } = HandoffStatus.NotRequested;

    public IntegrationMode? IntegrationMode { get; private set; }

    public string? ExternalCaseId { get; private set; }

    public HandoffStage? Stage { get; private set; }

    public AssignedSpecialist? AssignedSpecialist { get; private set; }

    public HandoffTerminalOutcome? Terminal { get; private set; }

    /// <summary>When the adapter acknowledged the submission — the anchor for status-poll TTL (support-adapter-v0.md §6.1).</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>True once status polling has reached `Support:StatusPoll:Ttl` without a terminal fact.</summary>
    public bool Stale { get; private set; }

    /// <summary>Dedupe key for status ingestion; -1 means no status has been applied yet.</summary>
    public long LastExternalRevision { get; private set; } = -1;

    internal Handoff(HandoffId id, CaseId caseId)
    {
        Id = id;
        CaseId = caseId;
    }

    internal void Confirm()
    {
        if (Status != HandoffStatus.NotRequested)
        {
            throw new InvalidHandoffTransitionException(Id, Status, HandoffStatus.Pending);
        }

        Status = HandoffStatus.Pending;
    }

    /// <summary>Accepted/SimulatedAccepted is reachable only from a pending submission acknowledged by the adapter.</summary>
    internal void Acknowledge(bool simulated, string? externalCaseId, DateTimeOffset now)
    {
        if (Status != HandoffStatus.Pending)
        {
            throw new InvalidHandoffTransitionException(Id, Status, HandoffStatus.Accepted);
        }

        Status = simulated ? HandoffStatus.SimulatedAccepted : HandoffStatus.Accepted;
        IntegrationMode = simulated ? Handoffs.IntegrationMode.Simulated : Handoffs.IntegrationMode.Real;
        ExternalCaseId = externalCaseId;
        AcceptedAt = now;
        Stale = false;
    }

    /// <summary>Status polling reached TTL without a terminal fact (support-adapter-v0.md §6.1).</summary>
    internal void MarkStale() => Stale = true;

    internal void MarkFailed()
    {
        if (Status != HandoffStatus.Pending)
        {
            throw new InvalidHandoffTransitionException(Id, Status, HandoffStatus.Failed);
        }

        Status = HandoffStatus.Failed;
    }

    internal void Retry()
    {
        if (Status != HandoffStatus.Failed)
        {
            throw new InvalidHandoffTransitionException(Id, Status, HandoffStatus.Pending);
        }

        Status = HandoffStatus.Pending;
    }

    /// <returns>false when the update duplicates an already-applied external revision and was ignored.</returns>
    internal bool IngestStatus(
        HandoffId handoffId,
        CaseId caseId,
        long externalRevision,
        HandoffStage? stage,
        AssignedSpecialist? assignedSpecialist,
        HandoffTerminalOutcome? terminal)
    {
        if (handoffId != Id || caseId != CaseId)
        {
            throw new HandoffMismatchException(Id, handoffId, CaseId, caseId);
        }

        if (externalRevision <= LastExternalRevision)
        {
            return false;
        }

        LastExternalRevision = externalRevision;
        Stale = false; // a fresh fact arrived, whatever channel it came from

        if (stage is not null)
        {
            Stage = stage;
        }

        if (assignedSpecialist is not null)
        {
            AssignedSpecialist = assignedSpecialist;
        }

        if (terminal is not null)
        {
            Terminal = terminal;
        }

        return true;
    }
}
