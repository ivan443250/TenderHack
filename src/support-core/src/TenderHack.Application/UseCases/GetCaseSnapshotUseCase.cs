using TenderHack.Application.Exceptions;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.UseCases;

/// <summary>Returns the Domain aggregate; shaping it into the `CaseSnapshot` wire JSON is `TenderHack.Api`'s job.</summary>
public sealed class GetCaseSnapshotUseCase(ICaseRepository cases)
{
    public async Task<Case> ExecuteAsync(CaseId caseId, string ownerId, CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new CaseNotFoundException(caseId);
        if (@case.OwnerId != ownerId)
        {
            throw new CaseNotFoundException(caseId);
        }

        return @case;
    }
}
