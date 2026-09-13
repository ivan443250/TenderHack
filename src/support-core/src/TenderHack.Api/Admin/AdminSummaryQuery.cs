using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Common;
using TenderHack.Infrastructure.Persistence;

namespace TenderHack.Api.Admin;

public sealed record AdminSummaryRequest(DateTimeOffset From, DateTimeOffset To, int Limit);

public sealed record AdminSummaryRange(DateTimeOffset From, DateTimeOffset To);

public sealed record AdminSummaryTotals(
    int CasesCreated,
    int CasesActive,
    int Turns,
    int UserMessages,
    int UniqueOwners,
    int HiddenCases);

public sealed record AdminFeedbackSummary(
    int Total,
    IReadOnlyDictionary<string, int> InformationQuality,
    IReadOnlyDictionary<string, int> Specialist,
    IReadOnlyDictionary<string, int> Solved);

public sealed record AdminDaySummary(string Date, int Turns, int Answers, int Clarifications, int Handoffs, int TechnicalErrors, int Moderation);

public sealed record AdminTopQuestion(string Text, int Count, IReadOnlyDictionary<string, int> Decisions);

public sealed record AdminQuestionRow(DateTimeOffset OccurredAt, Guid CaseId, Guid? TurnId, string Text, string? Decision, string? TurnStatus);

public sealed record AdminSummaryResponse(
    AdminSummaryRange Range,
    AdminSummaryTotals Totals,
    IReadOnlyDictionary<string, int> Decisions,
    IReadOnlyDictionary<string, int> TurnStatuses,
    IReadOnlyDictionary<string, int> HandoffStatuses,
    IReadOnlyDictionary<string, int> Completions,
    AdminFeedbackSummary Feedback,
    IReadOnlyList<AdminDaySummary> ByDay,
    IReadOnlyList<AdminTopQuestion> TopQuestions,
    IReadOnlyList<AdminQuestionRow> Questions,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Read-only aggregate over `api`-owned tables for the test admin panel. Everything is computed from
/// persisted facts (`cases`/`turns`/`case_events`/`feedback`) — no call to `knowledge`, no derived
/// product state. The period filter is the turn's `created_at` for question/decision statistics,
/// `cases.created_at` for "cases created", `completed_at` for completions and `submitted_at` for
/// feedback, so each number answers one unambiguous question about the window.
/// </summary>
public sealed partial class AdminSummaryQuery(TenderHackDbContext db, TimeProvider clock)
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;
    public const int TopQuestionsLimit = 20;

    public async Task<AdminSummaryResponse> ExecuteAsync(AdminSummaryRequest request, CancellationToken ct)
    {
        var (from, to, limit) = request;

        // Aggregates are small at hackathon scale and the owned `Turns`/`Handoff` come with the root,
        // so one query for "cases touched in the window" and in-memory grouping beats a handful of
        // owned-collection projections that EF may or may not translate identically across versions.
        var cases = await db.Cases.AsNoTracking()
            .Where(c => (c.CreatedAt >= from && c.CreatedAt < to)
                || (c.CompletedAt != null && c.CompletedAt >= from && c.CompletedAt < to)
                || c.Turns.Any(t => t.CreatedAt >= from && t.CreatedAt < to))
            .ToListAsync(ct);

        var userMessages = await db.CaseEvents.AsNoTracking()
            .Where(e => e.Type == "USER_MESSAGE" && e.OccurredAt >= from && e.OccurredAt < to)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(ct);

        var feedback = await db.Feedbacks.AsNoTracking()
            .Where(f => f.SubmittedAt >= from && f.SubmittedAt < to)
            .ToListAsync(ct);

        var turns = cases
            .SelectMany(c => c.Turns.Select(t => (Case: c, Turn: t)))
            .Where(x => x.Turn.CreatedAt >= from && x.Turn.CreatedAt < to)
            .ToList();
        var turnById = turns.ToDictionary(x => x.Turn.Id.Value, x => x.Turn);

        var casesCreated = cases.Where(c => c.CreatedAt >= from && c.CreatedAt < to).ToList();
        var totals = new AdminSummaryTotals(
            CasesCreated: casesCreated.Count,
            CasesActive: cases.Count(c => c.ConversationStatus == ConversationStatus.Active && c.HiddenAt is null),
            Turns: turns.Count,
            UserMessages: userMessages.Count,
            UniqueOwners: turns.Select(x => x.Case.OwnerId).Concat(casesCreated.Select(c => c.OwnerId)).Distinct().Count(),
            HiddenCases: cases.Count(c => c.HiddenAt is not null));

        var decisions = CountBy(turns.Select(x => x.Turn.Decision?.ToWire() ?? "NONE"));
        var turnStatuses = CountBy(turns.Select(x => x.Turn.Status.ToWire()));
        var handoffStatuses = CountBy(cases.Where(c => c.Handoff is not null).Select(c => c.Handoff!.Status.ToWire()));
        var completions = CountBy(cases
            .Where(c => c.CompletedAt is not null && c.CompletedAt >= from && c.CompletedAt < to)
            .Select(c => c.CompletionReason?.ToWire() ?? "UNKNOWN"));

        var feedbackSummary = new AdminFeedbackSummary(
            Total: feedback.Count,
            InformationQuality: CountBy(feedback.Select(f => f.InformationQualityRating?.ToWire() ?? "NONE")),
            Specialist: CountBy(feedback.Select(f => f.SpecialistRating?.ToWire() ?? "NONE")),
            Solved: CountBy(feedback.Select(f => f.Solved switch { true => "TRUE", false => "FALSE", null => "NULL" })));

        var byDay = turns
            .GroupBy(x => x.Turn.CreatedAt.UtcDateTime.Date)
            .OrderBy(g => g.Key)
            .Select(g => new AdminDaySummary(
                Date: g.Key.ToString("yyyy-MM-dd"),
                Turns: g.Count(),
                Answers: g.Count(x => x.Turn.Decision is Decision.Answer or Decision.AnswerAndHandoff),
                Clarifications: g.Count(x => x.Turn.Decision == Decision.Clarify),
                Handoffs: g.Count(x => x.Turn.Decision is Decision.HandoffOffer or Decision.AnswerAndHandoff),
                TechnicalErrors: g.Count(x => x.Turn.Decision == Decision.TechnicalError),
                Moderation: g.Count(x => x.Turn.Decision is Decision.ModerationWarning or Decision.ModerationClose)))
            .ToList();

        var questionRows = userMessages
            .Select(e =>
            {
                var turn = e.TurnId is { } turnId && turnById.TryGetValue(turnId, out var t) ? t : null;
                return new AdminQuestionRow(
                    e.OccurredAt,
                    e.CaseId,
                    e.TurnId,
                    ExtractText(e.PayloadJson),
                    turn?.Decision?.ToWire(),
                    turn?.Status.ToWire());
            })
            .ToList();

        var topQuestions = questionRows
            .GroupBy(q => Normalize(q.Text))
            .Where(g => g.Key.Length > 0)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(TopQuestionsLimit)
            .Select(g => new AdminTopQuestion(
                Text: g.First().Text,
                Count: g.Count(),
                Decisions: CountBy(g.Select(q => q.Decision ?? "NONE"))))
            .ToList();

        return new AdminSummaryResponse(
            new AdminSummaryRange(from, to),
            totals,
            decisions,
            turnStatuses,
            handoffStatuses,
            completions,
            feedbackSummary,
            byDay,
            topQuestions,
            questionRows.Take(limit).ToList(),
            clock.GetUtcNow());
    }

    private static IReadOnlyDictionary<string, int> CountBy(IEnumerable<string> keys) =>
        keys.GroupBy(k => k).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());

    private static string ExtractText(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    /// <summary>Grouping key for "same question asked again": case/whitespace/trailing punctuation insensitive, nothing smarter.</summary>
    private static string Normalize(string text)
    {
        var collapsed = WhitespaceRegex().Replace(text.Trim().ToLowerInvariant(), " ");
        return collapsed.TrimEnd('?', '!', '.', ' ');
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
