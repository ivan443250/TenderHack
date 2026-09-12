using TenderHack.Application.Exceptions;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.UseCases;

/// <summary>User-initiated completion (web-api-v0.md §9.1) — throws `CaseAlreadyCompletedException` when already closed.</summary>
public sealed class CompleteCaseUseCase(ICaseRepository cases, IUnitOfWork unitOfWork, CaseCompletionPublisher completionPublisher, TimeProvider clock)
{
    public async Task<Case> ExecuteAsync(CaseId caseId, string ownerId, bool? solved, CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new CaseNotFoundException(caseId);
        if (@case.OwnerId != ownerId)
        {
            throw new CaseNotFoundException(caseId);
        }

        var resolutionBefore = @case.ResolutionStatus;
        var now = clock.GetUtcNow();
        @case.CompleteByUser(solved, now);

        await completionPublisher.PublishAsync(@case, resolutionBefore, now, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return @case;
    }
}
