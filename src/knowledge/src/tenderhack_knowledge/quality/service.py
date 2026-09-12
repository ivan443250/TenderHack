"""Deterministic quality intake, evaluation and read-model services.

The module is deliberately transport-agnostic.  AI-5 can call the async
``QualityRepository`` from the existing routes/worker, while unit tests and
offline demos use ``InMemoryQualityStore``.  Both implementations apply the
same idempotency, evaluation and issue-grouping rules.
"""

from __future__ import annotations

import hashlib
import json
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Iterable, Mapping, Protocol

import sqlalchemy as sa
from sqlalchemy.dialects.postgresql import insert as pg_insert
from sqlalchemy.ext.asyncio import AsyncEngine

from tenderhack_knowledge.contracts.v0 import (
    DimensionScore,
    DimensionValue,
    Evaluation,
    IssueGroup,
    QualityCompletionPush,
    QualityFeedbackPush,
    QualityTurnPush,
)

from .schema import (
    issue_group_members,
    issue_groups,
    quality_cases,
    quality_completions,
    quality_evaluations,
    quality_feedback,
)


class QualityPayloadConflict(ValueError):
    """The same idempotency identity was delivered with different facts."""


@dataclass(frozen=True)
class IntakeResult:
    identity: str
    status: str  # stored | idempotent | updated
    persisted: bool = True


@dataclass(frozen=True)
class EvaluationRecord:
    case_id: str
    turn_id: str
    revision: int
    factual_support: Mapping[str, Any]
    completeness: Mapping[str, Any]
    clarity: Mapping[str, Any]
    next_step: Mapping[str, Any]
    critical_error: bool
    critical_error_reason: str | None
    provenance: Mapping[str, Any]
    evaluated_at: datetime

    def to_contract(self) -> Evaluation:
        return Evaluation(
            turn_id=self.turn_id,
            factual_support=DimensionScore.model_validate(self.factual_support),
            completeness=DimensionScore.model_validate(self.completeness),
            clarity=DimensionScore.model_validate(self.clarity),
            next_step=DimensionScore.model_validate(self.next_step),
            critical_error=self.critical_error,
            critical_error_reason=self.critical_error_reason,
            evaluated_at=self.evaluated_at,
        )


@dataclass(frozen=True)
class IssueGroupRecord:
    group_key: str
    label: str
    definition: str
    sample_size: int
    representative_case_ids: tuple[str, ...]
    negative_signal_count: int
    unresolved_count: int
    limitations: tuple[str, ...]
    hypotheses: tuple[Mapping[str, Any], ...]
    provenance: Mapping[str, Any]

    def to_contract(self) -> IssueGroup:
        # Hypothesis is validated by the frozen contract model at the boundary.
        return IssueGroup(
            group_id=self.group_key,
            label=self.label,
            n=self.sample_size,
            representative_case_ids=list(self.representative_case_ids),
            negative_signal_count=self.negative_signal_count,
            unresolved_count=self.unresolved_count,
            limitations=list(self.limitations),
            hypotheses=list(self.hypotheses),
        )


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


def _canonical_hash(value: Mapping[str, Any]) -> str:
    encoded = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), default=str).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _provenance(source_type: str, received_at: datetime, supplied: Mapping[str, Any] | None) -> dict[str, Any]:
    result = {"source_type": source_type, "received_at": received_at.isoformat()}
    if supplied:
        # Provenance is metadata, not an unrestricted copy of request headers.
        result.update({str(key): value for key, value in supplied.items() if key in {"trace_id", "producer", "source_reference"}})
    return result


def _turn_facts(payload: QualityTurnPush) -> dict[str, Any]:
    return payload.model_dump(mode="json")


def _feedback_facts(payload: QualityFeedbackPush) -> dict[str, Any]:
    return payload.model_dump(mode="json")


def _completion_facts(payload: QualityCompletionPush) -> dict[str, Any]:
    return payload.model_dump(mode="json")


def _score(value: DimensionValue, *, evidence: str | None = None, reason: str | None = None,
           limitation: str | None = None, improvement: str | None = None) -> dict[str, Any]:
    return {
        "value": value.value,
        "evidence": evidence,
        "reason": reason,
        "limitation": limitation,
        "improvement_suggestion": improvement,
    }


def evaluate_turn_facts(
    payload: QualityTurnPush | Mapping[str, Any],
    *,
    provenance: Mapping[str, Any] | None = None,
    evaluated_at: datetime | None = None,
) -> EvaluationRecord:
    """Apply a conservative, explainable rubric without an LLM.

    Evidence references permit only a *supported-reference* score.  They are
    not treated as proof of entailment; absent references therefore remain
    ``UNKNOWN`` rather than becoming a confident factual score.
    """

    facts = payload if isinstance(payload, Mapping) else _turn_facts(payload)
    answer = str(facts.get("answer_markdown") or "").strip()
    evidence_ids = tuple(str(item) for item in (facts.get("evidence_fragment_ids") or ()) if str(item).strip())
    error_category = str(facts.get("error_category") or "").strip()
    has_answer = bool(answer)
    has_evidence = bool(evidence_ids)
    evidence_text = ", ".join(evidence_ids) if has_evidence else None
    support_limitation = "Evidence references are present; entailment still requires source-level review." if has_evidence else "No normative evidence reference was supplied."

    if has_answer and has_evidence:
        factual = _score(
            DimensionValue.ONE,
            evidence=evidence_text,
            reason="The turn cites one or more evidence fragments.",
            limitation=support_limitation,
        )
        completeness = _score(
            DimensionValue.ONE,
            evidence=evidence_text,
            reason="An answer and evidence references are present.",
            limitation="Completeness of all procedural conditions is not inferred from payload shape.",
        )
        clarity = _score(DimensionValue.TWO, evidence="answer_markdown", reason="A non-empty answer was delivered.")
        next_step = _score(DimensionValue.TWO, evidence="answer_markdown", reason="The delivered answer can provide the user's next step.")
    elif has_answer and not has_evidence:
        factual = _score(DimensionValue.UNKNOWN, reason="Answer has no normative evidence reference.", limitation=support_limitation)
        completeness = _score(DimensionValue.UNKNOWN, reason="No evidence means procedural completeness cannot be assessed.")
        clarity = _score(DimensionValue.TWO, evidence="answer_markdown", reason="A non-empty answer was delivered.", limitation="Clarity is independent of factual support.")
        next_step = _score(DimensionValue.ONE, evidence="answer_markdown", reason="A next step may be present, but its grounding is unknown.", limitation="No evidence reference was supplied.")
    else:
        factual = _score(DimensionValue.UNKNOWN, reason="No answer was delivered.")
        completeness = _score(DimensionValue.UNKNOWN, reason="No answer is available for a completeness review.")
        clarity = _score(DimensionValue.NOT_APPLICABLE, reason="Clarity is not applicable without an answer.")
        next_step = _score(DimensionValue.NOT_APPLICABLE, reason="A next step is not applicable without an answer.")

    critical_error = bool(error_category)
    critical_reason = f"Pushed error category: {error_category}" if critical_error else None
    if critical_error:
        # A runtime error is a separate signal; it never changes dimension
        # values or turns missing evidence into a zero.
        for score in (factual, completeness, clarity, next_step):
            score.setdefault("limitation", "Technical error was reported separately from response quality.")
    return EvaluationRecord(
        case_id=str(facts["case_id"]),
        turn_id=str(facts["turn_id"]),
        revision=int(facts["revision"]),
        factual_support=factual,
        completeness=completeness,
        clarity=clarity,
        next_step=next_step,
        critical_error=critical_error,
        critical_error_reason=critical_reason,
        provenance=dict(provenance or {"source": "deterministic_quality_rubric", "evidence_ids": list(evidence_ids)}),
        evaluated_at=evaluated_at or _utc_now(),
    )


def _safe_group_key(facts: Mapping[str, Any]) -> tuple[str, str, str]:
    """Return a deterministic key using only supplied opaque attributes."""

    reason_codes = tuple(sorted(str(value).strip() for value in (facts.get("reason_codes") or ()) if str(value).strip()))
    service_need = str(facts.get("service_need") or "").strip()
    decision = str(facts.get("decision") or facts.get("decision_label") or "").strip()
    error = str(facts.get("error_category") or "").strip()
    if service_need:
        source, value = "service_need", service_need
    elif error:
        source, value = "error_category", error
    elif reason_codes:
        source, value = "reason_code", "|".join(reason_codes)
    elif decision:
        source, value = "decision_label", decision
    else:
        source, value = "unclassified", "UNCLASSIFIED"
    canonical = f"{source}:{value}".casefold()
    digest = hashlib.sha256(canonical.encode("utf-8")).hexdigest()[:16]
    return f"{source}:{digest}", source, value


def build_issue_groups(
    turns: Iterable[Mapping[str, Any]],
    feedback: Iterable[Mapping[str, Any]] = (),
    completions: Iterable[Mapping[str, Any]] = (),
    *,
    min_group_size: int = 2,
    generated_at: datetime | None = None,
) -> tuple[IssueGroupRecord, ...]:
    """Build conservative repeated-issue groups from pushed facts only."""

    turn_rows = sorted((dict(row) for row in turns), key=lambda row: (str(row.get("case_id", "")), str(row.get("turn_id", "")), int(row.get("revision", 0))))
    feedback_by_case: dict[str, list[Mapping[str, Any]]] = defaultdict(list)
    for row in feedback:
        feedback_by_case[str(row.get("case_id", ""))].append(row)
    completion_by_case = {str(row.get("case_id", "")): row for row in completions}
    grouped: dict[str, list[Mapping[str, Any]]] = defaultdict(list)
    labels: dict[str, tuple[str, str, str]] = {}
    for row in turn_rows:
        key, source, value = _safe_group_key(row)
        grouped[key].append(row)
        labels[key] = (source, value, key)

    result: list[IssueGroupRecord] = []
    for key in sorted(grouped):
        rows = grouped[key]
        if len(rows) < min_group_size:
            continue
        source, value, _ = labels[key]
        case_ids = tuple(sorted({str(row.get("case_id", "")) for row in rows if row.get("case_id")}))
        negative = sum(
            1
            for case_id in case_ids
            for row in feedback_by_case.get(case_id, ())
            if str(row.get("information_quality_rating") or "").upper() == "NEGATIVE"
        )
        unresolved = sum(
            1
            for case_id in case_ids
            if str((completion_by_case.get(case_id) or {}).get("resolution_status") or "").upper() in {"UNRESOLVED", "NOT_RESOLVED"}
        )
        limitations = (
            "Repeated supplied attributes are not proof of a product defect.",
            "Group is based on pushed facts; missing feedback/outcome is unknown.",
            f"n={len(rows)} is a descriptive sample, not a population statistic.",
        )
        hypothesis_type = "KNOWLEDGE_GAP" if source == "error_category" and "KNOW" in value.upper() else "OTHER"
        hypotheses = (
            {
                "type": hypothesis_type,
                "text": f"Hypothesis only: repeated {source}={value!r} may indicate a recurring support issue; review representative cases.",
                "confidence": "LOW",
            },
        )
        provenance = {"source": "pushed_quality_facts", "grouping": source}
        if generated_at is not None:
            provenance["generated_at"] = generated_at.isoformat()
        result.append(
            IssueGroupRecord(
                group_key=key,
                label=f"Repeated {source}: {value}",
                definition=f"Turns sharing the supplied opaque {source} value {value!r}.",
                sample_size=len(rows),
                representative_case_ids=case_ids[:3],
                negative_signal_count=negative,
                unresolved_count=unresolved,
                limitations=limitations,
                hypotheses=hypotheses,
                provenance=provenance,
            )
        )
    return tuple(result)


class QualityStore(Protocol):
    def ingest_turn(self, payload: QualityTurnPush, *, source_type: str = "api-worker", received_at: datetime | None = None, provenance: Mapping[str, Any] | None = None) -> IntakeResult: ...
    def ingest_feedback(self, payload: QualityFeedbackPush, *, source_type: str = "api-worker", received_at: datetime | None = None, provenance: Mapping[str, Any] | None = None) -> IntakeResult: ...
    def ingest_completion(self, payload: QualityCompletionPush, *, source_type: str = "api-worker", received_at: datetime | None = None, provenance: Mapping[str, Any] | None = None) -> IntakeResult: ...


class InMemoryQualityStore:
    """Deterministic fixture store mirroring the durable repository semantics."""

    def __init__(self) -> None:
        self.turns: dict[str, dict[str, Any]] = {}
        self.feedback: dict[str, dict[str, Any]] = {}
        self.completions: dict[str, dict[str, Any]] = {}
        self.evaluations: dict[str, EvaluationRecord] = {}
        self.groups: tuple[IssueGroupRecord, ...] = ()

    @staticmethod
    def _put(target: dict[str, dict[str, Any]], identity: str, facts: dict[str, Any]) -> IntakeResult:
        existing = target.get(identity)
        if existing is not None:
            if existing["payload_hash"] != facts["payload_hash"]:
                raise QualityPayloadConflict(f"conflicting payload for idempotency identity {identity}")
            return IntakeResult(identity, "idempotent")
        target[identity] = facts
        return IntakeResult(identity, "stored")

    def ingest_turn(self, payload: QualityTurnPush, *, source_type: str = "api-worker", received_at: datetime | None = None, provenance: Mapping[str, Any] | None = None) -> IntakeResult:
        received = received_at or _utc_now()
        identity = f"{payload.turn_id}:{payload.revision}"
        facts = _turn_facts(payload)
        facts.update(payload_hash=_canonical_hash(facts), source_type=source_type, received_at=received, provenance=_provenance(source_type, received, provenance))
        return self._put(self.turns, identity, facts)

    def ingest_feedback(self, payload: QualityFeedbackPush, *, source_type: str = "api-worker", received_at: datetime | None = None, provenance: Mapping[str, Any] | None = None) -> IntakeResult:
        received = received_at or _utc_now()
        facts = _feedback_facts(payload)
        facts.update(payload_hash=_canonical_hash(facts), source_type=source_type, received_at=received, provenance=_provenance(source_type, received, provenance))
        return self._put(self.feedback, payload.feedback_id, facts)

    def ingest_completion(self, payload: QualityCompletionPush, *, source_type: str = "api-worker", received_at: datetime | None = None, provenance: Mapping[str, Any] | None = None) -> IntakeResult:
        received = received_at or _utc_now()
        facts = _completion_facts(payload)
        facts.update(payload_hash=_canonical_hash(facts), source_type=source_type, received_at=received, provenance=_provenance(source_type, received, provenance))
        existing = self.completions.get(payload.case_id)
        if existing is None:
            self.completions[payload.case_id] = facts
            return IntakeResult(payload.case_id, "stored")
        if existing["payload_hash"] == facts["payload_hash"]:
            return IntakeResult(payload.case_id, "idempotent")
        # The frozen completion contract permits a later adapter terminal fact
        # to refresh the case projection.  An older/equal event with changed
        # content is still a conflict rather than an arbitrary overwrite.
        if payload.completed_at <= datetime.fromisoformat(str(existing["completed_at"])):
            raise QualityPayloadConflict(f"conflicting payload for idempotency identity {payload.case_id}")
        self.completions[payload.case_id] = facts
        return IntakeResult(payload.case_id, "updated")

    def evaluate_turn(self, turn_id: str, revision: int) -> EvaluationRecord:
        identity = f"{turn_id}:{revision}"
        if identity not in self.turns:
            raise KeyError(identity)
        if identity in self.evaluations:
            return self.evaluations[identity]
        record = evaluate_turn_facts(self.turns[identity], provenance={"source": "quality_cases", "delivery_key": identity})
        self.evaluations[identity] = record
        return record

    def rebuild_issue_groups(self, *, min_group_size: int = 2) -> tuple[IssueGroupRecord, ...]:
        self.groups = build_issue_groups(self.turns.values(), self.feedback.values(), self.completions.values(), min_group_size=min_group_size)
        return self.groups

    def evaluation_query(self, case_id: str) -> tuple[EvaluationRecord, ...]:
        return tuple(sorted((item for item in self.evaluations.values() if item.case_id == case_id), key=lambda item: (item.turn_id, item.revision)))

    def issue_groups_query(self) -> tuple[IssueGroupRecord, ...]:
        return self.groups

    def summary(self) -> dict[str, Any]:
        return {
            "turn_count": len(self.turns),
            "feedback_count": len(self.feedback),
            "completion_count": len(self.completions),
            "evaluation_count": len(self.evaluations),
            "issue_group_count": len(self.groups),
            "specialist_feedback_distribution": dict(Counter(str(row.get("specialist_rating")) for row in self.feedback.values() if row.get("specialist_rating"))),
            "information_quality_distribution": dict(Counter(str(row.get("information_quality_rating")) for row in self.feedback.values() if row.get("information_quality_rating"))),
            "limitations": ["Aggregates describe supplied signals and do not infer causality or employee performance."],
        }


class QualityRepository:
    """Async SQLAlchemy Core repository for the quality-owned tables."""

    def __init__(self, engine: AsyncEngine) -> None:
        self.engine = engine

    async def _ingest(self, table: sa.Table, identity: str, values: dict[str, Any], *, conflict_columns: list[str]) -> IntakeResult:
        async with self.engine.begin() as connection:
            existing = (await connection.execute(sa.select(table).where(table.c[conflict_columns[0]] == values[conflict_columns[0]]))).mappings().first()
            if existing is not None:
                if existing["payload_hash"] != values["payload_hash"]:
                    raise QualityPayloadConflict(f"conflicting payload for idempotency identity {identity}")
                return IntakeResult(identity, "idempotent")
            statement = pg_insert(table).values(values).on_conflict_do_nothing(index_elements=conflict_columns)
            await connection.execute(statement)
            row = (await connection.execute(sa.select(table).where(table.c[conflict_columns[0]] == values[conflict_columns[0]]))).mappings().first()
            if row is None:  # pragma: no cover - a transaction cannot lose its own insert
                raise RuntimeError(f"quality payload was not persisted: {identity}")
            if row["payload_hash"] != values["payload_hash"]:
                raise QualityPayloadConflict(f"conflicting payload for idempotency identity {identity}")
            return IntakeResult(identity, "stored")

    async def ingest_turn(self, payload: QualityTurnPush, *, source_type: str = "api-worker", received_at: datetime | None = None, provenance: Mapping[str, Any] | None = None) -> IntakeResult:
        received = received_at or _utc_now()
        facts = _turn_facts(payload)
        identity = f"{payload.turn_id}:{payload.revision}"
        values = {
            "delivery_key": identity, "case_id": payload.case_id, "turn_id": payload.turn_id, "revision": payload.revision,
            "source_type": source_type, "received_at": received, "occurred_at": payload.occurred_at,
            "payload_hash": _canonical_hash(facts), "provenance": _provenance(source_type, received, provenance),
            "question_text": payload.question_text, "prior_turns_summary": payload.prior_turns_summary,
            "decision_label": payload.decision, "reason_codes": payload.reason_codes, "answer_markdown": payload.answer_markdown,
            "evidence_fragment_ids": payload.evidence_fragment_ids, "snapshot_id": payload.snapshot_id,
            "handoff_status_label": payload.handoff_status, "recommended_line": payload.recommended_line,
            "service_need": payload.service_need, "stage_timings": payload.stage_timings.model_dump(mode="json"),
            "error_category": payload.error_category, "created_at": received,
        }
        return await self._ingest(quality_cases, identity, values, conflict_columns=["delivery_key"])

    async def ingest_feedback(self, payload: QualityFeedbackPush, *, source_type: str = "api-worker", received_at: datetime | None = None, provenance: Mapping[str, Any] | None = None) -> IntakeResult:
        received = received_at or _utc_now()
        values = {
            "feedback_id": payload.feedback_id, "case_id": payload.case_id, "turn_id": payload.turn_id, "source_type": source_type,
            "received_at": received, "occurred_at": payload.occurred_at, "payload_hash": _canonical_hash(_feedback_facts(payload)),
            "provenance": _provenance(source_type, received, provenance), "specialist_rating": payload.specialist_rating.value if payload.specialist_rating else None,
            "information_quality_rating": payload.information_quality_rating.value if payload.information_quality_rating else None,
            "helpful": payload.helpful, "specialist_ref": payload.specialist_ref, "integration_mode": payload.integration_mode.value if payload.integration_mode else None,
            "solved": payload.solved, "comment_text": payload.comment_text, "created_at": received,
        }
        return await self._ingest(quality_feedback, payload.feedback_id, values, conflict_columns=["feedback_id"])

    async def ingest_completion(self, payload: QualityCompletionPush, *, source_type: str = "api-worker", received_at: datetime | None = None, provenance: Mapping[str, Any] | None = None) -> IntakeResult:
        received = received_at or _utc_now()
        values = {
            "case_id": payload.case_id, "completed_at": payload.completed_at, "source_type": source_type, "received_at": received,
            "payload_hash": _canonical_hash(_completion_facts(payload)), "provenance": _provenance(source_type, received, provenance),
            "completion_reason": payload.completion_reason, "resolution_status": payload.resolution_status, "handoff_status": payload.handoff_status,
            "integration_mode": payload.integration_mode.value if payload.integration_mode else None, "specialist_ref": payload.specialist_ref,
            "stage_code": payload.stage_code, "moderation_warning_count": payload.moderation_warning_count, "turn_count": payload.turn_count,
            "created_at": received,
        }
        async with self.engine.begin() as connection:
            existing = (await connection.execute(sa.select(quality_completions).where(quality_completions.c.case_id == payload.case_id))).mappings().first()
            if existing is None:
                await connection.execute(pg_insert(quality_completions).values(values))
                return IntakeResult(payload.case_id, "stored")
            if existing["payload_hash"] == values["payload_hash"]:
                return IntakeResult(payload.case_id, "idempotent")
            if payload.completed_at <= existing["completed_at"]:
                raise QualityPayloadConflict(f"conflicting payload for idempotency identity {payload.case_id}")
            await connection.execute(sa.update(quality_completions).where(quality_completions.c.case_id == payload.case_id).values(**values))
            return IntakeResult(payload.case_id, "updated")

    async def evaluate_turn(self, turn_id: str, revision: int) -> EvaluationRecord:
        identity = f"{turn_id}:{revision}"
        async with self.engine.begin() as connection:
            row = (await connection.execute(sa.select(quality_cases).where(quality_cases.c.delivery_key == identity))).mappings().first()
            if row is None:
                raise KeyError(identity)
            existing_evaluation = (await connection.execute(sa.select(quality_evaluations).where(quality_evaluations.c.evaluation_id == identity))).mappings().first()
            if existing_evaluation is not None:
                return EvaluationRecord(
                    case_id=existing_evaluation["case_id"], turn_id=existing_evaluation["turn_id"], revision=existing_evaluation["revision"],
                    factual_support=existing_evaluation["factual_support"], completeness=existing_evaluation["completeness"],
                    clarity=existing_evaluation["clarity"], next_step=existing_evaluation["next_step"],
                    critical_error=existing_evaluation["critical_error"], critical_error_reason=existing_evaluation["critical_error_reason"],
                    provenance=existing_evaluation["provenance"], evaluated_at=existing_evaluation["evaluated_at"],
                )
            record = evaluate_turn_facts(row, provenance={"source": "quality_cases", "delivery_key": identity})
            values = {
                "evaluation_id": identity, "case_id": record.case_id, "turn_id": record.turn_id, "revision": record.revision,
                "factual_support": dict(record.factual_support), "completeness": dict(record.completeness), "clarity": dict(record.clarity),
                "next_step": dict(record.next_step), "critical_error": record.critical_error, "critical_error_reason": record.critical_error_reason,
                "provenance": dict(record.provenance), "evaluated_at": record.evaluated_at,
            }
            await connection.execute(pg_insert(quality_evaluations).values(values).on_conflict_do_update(index_elements=["evaluation_id"], set_=values))
            return record

    async def evaluations_for_case(self, case_id: str) -> tuple[EvaluationRecord, ...]:
        async with self.engine.connect() as connection:
            rows = (await connection.execute(sa.select(quality_evaluations).where(quality_evaluations.c.case_id == case_id).order_by(quality_evaluations.c.turn_id, quality_evaluations.c.revision))).mappings()
            return tuple(EvaluationRecord(case_id=row["case_id"], turn_id=row["turn_id"], revision=row["revision"], factual_support=row["factual_support"], completeness=row["completeness"], clarity=row["clarity"], next_step=row["next_step"], critical_error=row["critical_error"], critical_error_reason=row["critical_error_reason"], provenance=row["provenance"], evaluated_at=row["evaluated_at"]) for row in rows)

    async def issue_groups_query(self) -> tuple[IssueGroupRecord, ...]:
        async with self.engine.connect() as connection:
            rows = (await connection.execute(sa.select(issue_groups).order_by(issue_groups.c.group_key))).mappings().all()
            return tuple(
                IssueGroupRecord(
                    group_key=row["group_key"], label=row["label"], definition=row["definition"], sample_size=row["sample_size"],
                    representative_case_ids=tuple(row["representative_case_ids"] or ()), negative_signal_count=row["negative_signal_count"],
                    unresolved_count=row["unresolved_count"], limitations=tuple(row["limitations"] or ()), hypotheses=tuple(row["hypotheses"] or ()),
                    provenance=row["provenance"],
                )
                for row in rows
            )

    async def rebuild_issue_groups(self, *, min_group_size: int = 2) -> tuple[IssueGroupRecord, ...]:
        async with self.engine.begin() as connection:
            turns = (await connection.execute(sa.select(quality_cases))).mappings().all()
            feedback = (await connection.execute(sa.select(quality_feedback))).mappings().all()
            completions = (await connection.execute(sa.select(quality_completions))).mappings().all()
            groups = build_issue_groups(turns, feedback, completions, min_group_size=min_group_size)
            await connection.execute(sa.delete(issue_group_members))
            await connection.execute(sa.delete(issue_groups))
            for group in groups:
                await connection.execute(pg_insert(issue_groups).values(group_key=group.group_key, label=group.label, definition=group.definition, sample_size=group.sample_size, representative_case_ids=list(group.representative_case_ids), negative_signal_count=group.negative_signal_count, unresolved_count=group.unresolved_count, limitations=list(group.limitations), hypotheses=list(group.hypotheses), provenance=dict(group.provenance), generated_at=_utc_now()))
                for row in turns:
                    key, _source, _value = _safe_group_key(row)
                    if key == group.group_key:
                        await connection.execute(issue_group_members.insert().values(group_key=key, delivery_key=row["delivery_key"], case_id=row["case_id"]))
            return groups

    async def summary(self) -> dict[str, Any]:
        async with self.engine.connect() as connection:
            counts = {}
            for name, table in (("turn_count", quality_cases), ("feedback_count", quality_feedback), ("completion_count", quality_completions), ("evaluation_count", quality_evaluations), ("issue_group_count", issue_groups)):
                counts[name] = int((await connection.execute(sa.select(sa.func.count()).select_from(table))).scalar_one())
            counts["limitations"] = ["Aggregates describe supplied signals and do not infer causality or employee performance."]
            return counts


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
