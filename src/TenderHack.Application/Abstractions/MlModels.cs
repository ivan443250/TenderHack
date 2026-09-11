using TenderHack.Domain.Enums;
using TenderHack.Domain.ValueObjects;

namespace TenderHack.Application.Abstractions;

public sealed record TriageResult(
    bool IsProfane,
    double ToxicityScore,
    SupportLine Line,
    double LineConfidence,
    string? Topic);

public sealed record AnswerResult(
    string Text,
    IReadOnlyList<SourceRef> Sources,
    double Confidence,
    bool NoAnswer,
    IReadOnlyDictionary<string, double> Timings);

public sealed record FeedbackText(Guid TicketId, string Comment);

public sealed record ProblemCluster(
    string Label,
    int Count,
    double Share,
    IReadOnlyList<string> ExampleComments);
