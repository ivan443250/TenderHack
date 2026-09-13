using TenderHack.Application.Exceptions;
using TenderHack.Application.Knowledge;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Common;
using TenderHack.Domain.Feedback;

namespace TenderHack.Application.UseCases;

/// <summary>
/// Four independent signals, once per case, only after completion (web-api-v0.md §9.3). Only
/// `solved` can move resolution — `Case.RecordFeedback` enforces that, this use-case never touches
/// `ResolutionStatus` directly. Also enqueues the `quality.feedback` push (knowledge-v0.md §10).
/// </summary>
public sealed class SubmitFeedbackUseCase(
    ICaseRepository cases, IFeedbackRepository feedbackRepository, IUnitOfWork unitOfWork,
    ITurnEventStream events, IOutbox outbox, TimeProvider clock)
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

        var resolutionBefore = @case.ResolutionStatus;
        @case.RecordFeedback(feedback.Id, solved);
        feedbackRepository.Add(feedback);

        await events.PublishAsync(caseId, new CaseEvent("FEEDBACK_SUBMITTED", null, null, now, new Dictionary<string, object?>
        {
            ["specialist_rating"] = specialistRating.ToWire(),
            ["information_quality_rating"] = informationQualityRating.ToWire(),
            ["solved"] = solved,
        }), ct);

        // `solved` is the one feedback signal that can move resolution (from UNKNOWN only); the
        // timeline must show that transition the same way user/support completion does.
        if (@case.ResolutionStatus != resolutionBefore)
        {
            await events.PublishAsync(caseId, new CaseEvent("CASE_RESOLUTION_CHANGED", null, null, now,
                new Dictionary<string, object?> { ["resolution_status"] = @case.ResolutionStatus.ToWire() }), ct);
        }

        var turnId = @case.Turns.Count > 0 ? @case.Turns[^1].Id.ToString() : caseId.ToString();
        outbox.Enqueue(QualityOutboxMessages.Feedback, new QualityFeedbackPush(
            feedback.Id.ToString(),
            caseId.ToString(),
            turnId,
            now,
            specialistRating,
            informationQualityRating,
            SpecialistRef: @case.Handoff?.AssignedSpecialist?.Ref,
            IntegrationMode: @case.Handoff?.IntegrationMode.ToWire(),
            solved,
            commentText));

        await unitOfWork.SaveChangesAsync(ct);
        return feedback;
    }
}
