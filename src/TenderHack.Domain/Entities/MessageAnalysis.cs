using TenderHack.Domain.Enums;

namespace TenderHack.Domain.Entities;

// Owned by Message (OwnsOne in EF config) — the triage/answer verdict for a single message.
public sealed class MessageAnalysis
{
    public bool IsProfane { get; private set; }
    public double ToxicityScore { get; private set; }
    public SupportLine? Line { get; private set; }
    public double LineConfidence { get; private set; }
    public string? Topic { get; private set; }
    public double? AnswerConfidence { get; private set; }
    public bool NoAnswer { get; private set; }

    private MessageAnalysis() { }

    public static MessageAnalysis FromTriage(
        bool isProfane,
        double toxicityScore,
        SupportLine? line,
        double lineConfidence,
        string? topic) => new()
        {
            IsProfane = isProfane,
            ToxicityScore = toxicityScore,
            Line = line,
            LineConfidence = lineConfidence,
            Topic = topic
        };

    public void AttachAnswerVerdict(double? confidence, bool noAnswer)
    {
        AnswerConfidence = confidence;
        NoAnswer = noAnswer;
    }
}
