using TenderHack.Application.Exceptions;
using TenderHack.Application.Handoff;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.UseCases;

/// <summary>
/// Persists the handoff as `PENDING` and enqueues its submission atomically (web-api-v0.md §8).
/// The actual `IHandoffAdapter.SubmitAsync` call happens later, in `api-worker`'s outbox consumer —
/// never synchronously in this request.
/// </summary>
public sealed class ConfirmHandoffUseCase(ICaseRepository cases, IUnitOfWork unitOfWork, IOutbox outbox)
{
    public async Task<Case> ExecuteAsync(
        CaseId caseId,
        string ownerId,
        string summary,
        string dispatchQueue,
        IReadOnlyList<string> reasonCodes,
        bool engineeringReviewSuggested,
        CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new CaseNotFoundException(caseId);
        if (@case.OwnerId != ownerId)
        {
            throw new CaseNotFoundException(caseId);
        }

        @case.ConfirmHandoff();
        outbox.Enqueue(HandoffOutboxMessages.Submit, new HandoffSubmitPayload(
            @case.Id.ToString(), @case.Handoff!.Id.ToString(), summary, dispatchQueue, reasonCodes, engineeringReviewSuggested));

        await unitOfWork.SaveChangesAsync(ct);
        return @case;
    }
}
