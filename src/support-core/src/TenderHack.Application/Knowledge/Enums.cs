namespace TenderHack.Application.Knowledge;

/// <summary>knowledge-v0.md §5.1 — always explicit; NORMATIVE for user-facing answers, HISTORICAL only for analytics.</summary>
public enum Corpus
{
    Normative,
    Historical,
}

public enum EvidenceSufficiency
{
    Sufficient,
    ConditionDependent,
    Insufficient,
}

/// <summary>knowledge-v0.md §3 — classification of any failed `api → knowledge` call, never a decision by itself.</summary>
public enum KnowledgeFailureCategory
{
    Timeout,
    Unavailable,
    InvalidResponse,
    ModelError,
}
