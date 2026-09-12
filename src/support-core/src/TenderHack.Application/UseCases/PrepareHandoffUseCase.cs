using TenderHack.Application.Exceptions;
using TenderHack.Application.Handoff;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.UseCases;

/// <summary>
/// Creates the handoff row without submitting it (web-api-v0.md §8: "does not set HandoffStatus=PENDING").
/// The package is assembled from the case's own persisted event history — no `knowledge` call
/// (support-adapter-v0.md §2), so this works even when `knowledge` is unavailable.
/// </summary>
public sealed class PrepareHandoffUseCase(ICaseRepository cases, ICaseEventReader events, IUnitOfWork unitOfWork)
{
    public async Task<Case> ExecuteAsync(CaseId caseId, string ownerId, CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new CaseNotFoundException(caseId);
        if (@case.OwnerId != ownerId)
        {
            throw new CaseNotFoundException(caseId);
        }

        var history = await events.ListAsync(caseId, after: 0, ct);
        var package = HandoffPackageBuilder.Build(@case, history);

        @case.PrepareHandoff(package);
        await unitOfWork.SaveChangesAsync(ct);
        return @case;
    }
}
