using TenderHack.Domain.Cases;

namespace TenderHack.Application.Knowledge;

/// <summary>
/// Trace context every staged `knowledge` call carries (knowledge-v0.md §2). One <see cref="TraceId"/>
/// per turn end-to-end — the same value on every stage call for that turn, not a fresh one per call.
/// </summary>
public sealed record KnowledgeRequestContext(Guid TraceId, CaseId CaseId, TurnId TurnId);

public sealed record UnderstandRequest(string Text, string? PriorTurnSummary);

/// <summary>
/// knowledge-v0.md — a context slot (role, process, edo_provider, document_type, status, user_action,
/// error_code, duration, already_tried, ...). Never flatten this to a bare string: `Provenance`
/// distinguishes what the user actually said from what was inferred, and `Type` is what retrieval
/// exact-matches against.
/// </summary>
public sealed record Entity(string Type, string Value, EntityProvenance Provenance);

public enum EntityProvenance
{
    UserExplicit,
    TrustedPortalContext,
    Inferred,
    Unknown,
}

public sealed record UnderstandResult(
    string NormalizedText,
    IReadOnlyList<Entity> Entities,
    IReadOnlyList<string> ExactCodes,
    IReadOnlyList<string> LanguageFlags);

/// <summary><see cref="Start"/>/<see cref="End"/> are the rule engine's own offsets into its normalized text (knowledge-v0 `matched_span`).</summary>
public sealed record ModerationContextRequest(string Text, string RuleId, string RuleVersion, string MatchedTerm, int Start, int End);

public enum ModerationAmbiguity
{
    Offensive,
    NotOffensive,
    Uncertain,
}

public sealed record ModerationContextResult(ModerationAmbiguity Ambiguity, string ModelVersion);

public sealed record RetrieveRequest(
    string Query,
    IReadOnlyList<Entity> Entities,
    IReadOnlyList<string> ExactCodes,
    Corpus Corpus,
    string? SnapshotId);

public sealed record RetrievalCandidate(string FragmentId, string DocumentId, int? Page, string? Anchor, string? Title = null);

public sealed record RetrieveResult(
    string SnapshotId,
    string RetrievalConfigVersion,
    IReadOnlyList<RetrievalCandidate> Candidates);

public sealed record AnswerabilityRequest(string Query, string SnapshotId, IReadOnlyList<string> CandidateFragmentIds);

public sealed record AnswerabilityResult(
    EvidenceSufficiency EvidenceSufficiency,
    IReadOnlyList<string> EvidenceFragmentIds,
    IReadOnlyList<string> MissingConditions,
    IReadOnlyList<string> RiskFlags);

/// <summary>
/// `Tone` maps to `knowledge-v0`'s existing (already-frozen, previously unused) `DraftConstraints.tone`
/// field — product-spec.md §12's "один ограниченный rewrite/экстрактивный fallback" passes
/// `"extractive"` on the retry after a failed verification, asking for a more conservative,
/// closer-to-source draft instead of giving up after the first attempt.
/// </summary>
public sealed record DraftRequest(string Query, string SnapshotId, IReadOnlyList<string> EvidenceFragmentIds, string? Tone = null);

public sealed record DraftClaim(string ClaimId, string Text, IReadOnlyList<string> FragmentIds);

public sealed record DraftResult(string Markdown, IReadOnlyList<DraftClaim> Claims, string ModelVersion);

public sealed record VerifyRequest(IReadOnlyList<DraftClaim> Claims, string SnapshotId);

public sealed record ClaimVerification(string ClaimId, bool Supported, IReadOnlyList<string> EvidenceFragmentIds);

public sealed record VerifyResult(IReadOnlyList<ClaimVerification> Results);

public sealed record SourceFragment(
    string DocumentId,
    string Title,
    string Version,
    int? Page,
    string? Anchor,
    string Text,
    string SnapshotId);
