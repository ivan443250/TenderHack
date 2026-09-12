namespace TenderHack.Application.Handoff;

/// <summary>Outbox message shapes shared between the enqueuing use-cases and `api-worker`'s consumer.</summary>
public static class HandoffOutboxMessages
{
    public const string Submit = "handoff.submit";
}

/// <summary>
/// Everything `api-worker` needs to call <see cref="Ports.IHandoffAdapter.SubmitAsync"/> — the
/// idempotency key is the handoff id itself, stable across confirm and any later retry of the same
/// logical handoff (support-adapter-v0.md §3: "same logical handoff retry reuses the same stable
/// idempotency key").
/// </summary>
public sealed record HandoffSubmitPayload(
    string CaseId,
    string HandoffId,
    string Summary,
    string DispatchQueue,
    IReadOnlyList<string> ReasonCodes,
    bool EngineeringReviewSuggested);
