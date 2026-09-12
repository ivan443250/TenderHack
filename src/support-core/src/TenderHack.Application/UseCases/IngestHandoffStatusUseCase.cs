using TenderHack.Application.Exceptions;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Application.UseCases;

/// <summary>
/// Single write path for handoff status facts, whichever channel delivered them — polling or the
/// inbound webhook (support-adapter-v0.md §6: "the Domain never knows which channel delivered a fact").
/// </summary>
public sealed class IngestHandoffStatusUseCase(ICaseRepository cases, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task ExecuteAsync(
        CaseId caseId,
        HandoffId handoffId,
        long externalRevision,
        HandoffStage? stage,
        AssignedSpecialist? assignedSpecialist,
        HandoffTerminalOutcome? terminal,
        CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new CaseNotFoundException(caseId);
        @case.IngestHandoffStatus(handoffId, externalRevision, stage, assignedSpecialist, terminal, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
    }
}
