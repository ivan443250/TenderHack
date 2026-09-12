using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.UseCases;

public sealed class CreateCaseUseCase(ICaseRepository cases, IUnitOfWork unitOfWork)
{
    public async Task<Case> ExecuteAsync(string ownerId, CancellationToken ct)
    {
        var @case = new Case(CaseId.New(), ownerId);
        cases.Add(@case);
        await unitOfWork.SaveChangesAsync(ct);
        return @case;
    }
}
