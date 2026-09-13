using TenderHack.Application.Exceptions;
using TenderHack.Application.Orchestration;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.UseCases;

/// <summary>
/// E1 (docs/plans/active/2026-09-demo-readiness.md): soft-hide a case from the owner's own lists.
/// Never deletes anything — `Case.Hide` decides whether an active conversation needs completing
/// first, and this use case reuses `CaseCompletionPublisher` (without notification/feedback) so
/// there remains exactly one path that ever emits `CASE_COMPLETED`.
/// </summary>
public sealed class HideCaseUseCase(ICaseRepository cases, IUnitOfWork unitOfWork, CaseCompletionPublisher completionPublisher, TimeProvider clock)
{
    public async Task ExecuteAsync(CaseId caseId, string ownerId, CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new CaseNotFoundException(caseId);
        if (@case.OwnerId != ownerId)
        {
            throw new CaseNotFoundException(caseId);
        }

        var resolutionBefore = @case.ResolutionStatus;
        var wasActive = @case.ConversationStatus == ConversationStatus.Active;
        var now = clock.GetUtcNow();

        @case.Hide(now);

        if (wasActive)
        {
            await completionPublisher.PublishAsync(@case, resolutionBefore, now, ct, notify: false, requestFeedback: false);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
