using TenderHack.Application.Exceptions;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.UseCases;

public sealed class SendMessageUseCase(ICaseRepository cases, IUnitOfWork unitOfWork, TurnOrchestrator orchestrator)
{
    public async Task<TurnOutcome> ExecuteAsync(CaseId caseId, string ownerId, string text, CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new CaseNotFoundException(caseId);
        if (@case.OwnerId != ownerId)
        {
            throw new CaseNotFoundException(caseId);
        }

        var outcome = await orchestrator.RunAsync(@case, text, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return outcome;
    }
}
