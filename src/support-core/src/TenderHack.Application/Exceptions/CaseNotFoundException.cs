using TenderHack.Domain.Cases;

namespace TenderHack.Application.Exceptions;

/// <summary>
/// Raised both when a case truly does not exist and when the caller's owner cookie does not match
/// it — owner-scoping (architecture.md §17) must not let a non-owner distinguish the two.
/// </summary>
public sealed class CaseNotFoundException(CaseId caseId) : Exception($"Case {caseId} was not found.")
{
    public CaseId CaseId { get; } = caseId;
}
