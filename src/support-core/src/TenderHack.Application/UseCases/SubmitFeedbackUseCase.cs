using TenderHack.Application.Exceptions;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Feedback;

namespace TenderHack.Application.UseCases;

/// <summary>
/// Four independent signals, once per case, only after completion (web-api-v0.md §9.3). Only
/// `solved` can move resolution — `Case.RecordFeedback` enforces that, this use-case never touches
/// `ResolutionStatus` directly.
/// </summary>
public sealed class SubmitFeedbackUseCase(ICaseRepository cases, IFeedbackRepository feedbackRepository, IUnitOfWork unitOfWork, ITurnEventStream events, TimeProvider clock)
{
    public async Task<Feedback> ExecuteAsync(
        CaseId caseId,
        string ownerId,
        FeedbackRating? specialistRating,
        FeedbackRating? informationQualityRating,
        bool? solved,
        string? commentText,
        CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new CaseNotFoundException(caseId);
        if (@case.OwnerId != ownerId)
        {
            throw new CaseNotFoundException(caseId);
        }

        var now = clock.GetUtcNow();
        var feedback = new Feedback(FeedbackId.New(), caseId, specialistRating, informationQualityRating, solved, commentText, now);

        @case.RecordFeedback(feedback.Id, solved);
        feedbackRepository.Add(feedback);

        await events.PublishAsync(caseId, new CaseEvent("FEEDBACK_SUBMITTED", null, null, now, new Dictionary<string, object?>
        {
            ["specialist_rating"] = specialistRating?.ToString(),
            ["information_quality_rating"] = informationQualityRating?.ToString(),
            ["solved"] = solved,
        }), ct);

        await unitOfWork.SaveChangesAsync(ct);
        return feedback;
    }
}
