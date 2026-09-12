namespace TenderHack.Application.Handoff;

/// <summary>Outbox message shapes shared between the enqueuing use-cases and `api-worker`'s consumer.</summary>
public static class HandoffOutboxMessages
{
    public const string Submit = "handoff.submit";
}

/// <summary>
/// The identifier alone — `api-worker` reads the actual package/summary straight off the loaded
/// `Handoff` aggregate (`docs/plans/active/2026-09-support-core-completion.md` item A6: "data read
/// from the DB", not duplicated onto the outbox row where it could silently drift from what
/// `confirm`/`retry` actually persisted). The idempotency key for
/// <see cref="Ports.IHandoffAdapter.SubmitAsync"/> is the handoff id itself, stable across confirm
/// and any later retry of the same logical handoff (support-adapter-v0.md §3).
/// </summary>
public sealed record HandoffSubmitPayload(string CaseId, string HandoffId);
