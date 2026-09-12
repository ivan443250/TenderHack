"""Knowledge-owned durable quality analytics boundary.

The HTTP routes remain unchanged in AI-4.  Consumers use the service classes
here to persist pushed facts, run the deterministic rubric and build
read-only issue-group projections.
"""

from .service import (
    EvaluationRecord,
    InMemoryQualityStore,
    IntakeResult,
    IssueGroupRecord,
    QualityPayloadConflict,
    QualityRepository,
    build_issue_groups,
    evaluate_turn_facts,
)

__all__ = [
    "EvaluationRecord",
    "InMemoryQualityStore",
    "IntakeResult",
    "IssueGroupRecord",
    "QualityPayloadConflict",
    "QualityRepository",
    "build_issue_groups",
    "evaluate_turn_facts",
]
