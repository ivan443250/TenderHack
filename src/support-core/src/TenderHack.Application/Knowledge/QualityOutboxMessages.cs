namespace TenderHack.Application.Knowledge;

/// <summary>Outbox message types for the quality push endpoints (knowledge-v0.md §10). Payload is the push record itself.</summary>
public static class QualityOutboxMessages
{
    public const string Turn = "quality.turn";
    public const string Feedback = "quality.feedback";
    public const string Completion = "quality.completion";
}
