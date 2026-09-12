using TenderHack.Application.Exceptions;
using TenderHack.Application.Handoff;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.UseCases;

/// <summary>
/// Resubmits a `FAILED` handoff. The idempotency key is the handoff id itself — stable across the
/// original confirm and every retry of the same logical handoff (support-adapter-v0.md §3).
/// </summary>
public sealed class RetryHandoffUseCase(ICaseRepository cases, IUnitOfWork unitOfWork, IOutbox outbox)
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

        @case.RetryHandoff();
        outbox.Enqueue(HandoffOutboxMessages.Submit, new HandoffSubmitPayload(
            @case.Id.ToString(), @case.Handoff!.Id.ToString(), summary, dispatchQueue, reasonCodes, engineeringReviewSuggested));

        await unitOfWork.SaveChangesAsync(ct);
        return @case;
    }
}
