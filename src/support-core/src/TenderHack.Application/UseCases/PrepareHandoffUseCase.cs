using TenderHack.Application.Exceptions;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.UseCases;

/// <summary>Creates the handoff row without submitting it (web-api-v0.md §8: "does not set HandoffStatus=PENDING").</summary>
public sealed class PrepareHandoffUseCase(ICaseRepository cases, IUnitOfWork unitOfWork)
{
    public async Task<Case> ExecuteAsync(CaseId caseId, string ownerId, CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new CaseNotFoundException(caseId);
        if (@case.OwnerId != ownerId)
        {
            throw new CaseNotFoundException(caseId);
        }

        @case.PrepareHandoff();
        await unitOfWork.SaveChangesAsync(ct);
        return @case;
    }
}
