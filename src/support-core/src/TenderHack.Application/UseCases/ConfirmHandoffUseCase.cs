using TenderHack.Application.Exceptions;
using TenderHack.Application.Handoff;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.UseCases;

/// <summary>
/// Persists the handoff as `PENDING` and enqueues its submission atomically (web-api-v0.md §8).
/// The actual `IHandoffAdapter.SubmitAsync` call happens later, in `api-worker`'s outbox consumer —
/// never synchronously in this request. `summary` is the only field the caller edits; every other
/// fact was already assembled into `Handoff.Package` at `prepare` time.
/// </summary>
public sealed class ConfirmHandoffUseCase(ICaseRepository cases, IUnitOfWork unitOfWork, IOutbox outbox)
{
    public async Task<Case> ExecuteAsync(CaseId caseId, string ownerId, string summary, CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new CaseNotFoundException(caseId);
        if (@case.OwnerId != ownerId)
        {
            throw new CaseNotFoundException(caseId);
        }

        @case.ConfirmHandoff(summary);
        outbox.Enqueue(HandoffOutboxMessages.Submit, new HandoffSubmitPayload(@case.Id.ToString(), @case.Handoff!.Id.ToString()));

        await unitOfWork.SaveChangesAsync(ct);
        return @case;
    }
}
