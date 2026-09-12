using TenderHack.Domain.Cases;

namespace TenderHack.Domain.Feedback;

public enum FeedbackRating
{
    Positive,
    Negative,
}

/// <summary>
/// Four independent user signals recorded once per case (product-spec.md §18, web-api-v0.md §9.3).
/// Immutable once submitted — there is no "edit feedback" concept.
/// </summary>
public sealed record Feedback(
    FeedbackId Id,
    CaseId CaseId,
    FeedbackRating? SpecialistRating,
    FeedbackRating? InformationQualityRating,
    bool? Solved,
    string? CommentText,
    DateTimeOffset SubmittedAt);
