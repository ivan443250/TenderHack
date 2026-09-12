"""Snapshot/corpus/source verification for condition cards."""

from __future__ import annotations

import inspect
from collections import Counter
from collections.abc import Iterable
from typing import Any, Protocol

from pydantic import BaseModel, ConfigDict, Field

from tenderhack_knowledge.ingestion.models import Corpus, KnowledgeFragment, KnowledgeSnapshot

from .cards import ConditionCard, ConditionCardRisk


class ConditionCardRepository(Protocol):
    def get_snapshot(self, snapshot_id: str) -> KnowledgeSnapshot | None: ...

    def snapshot_fragments(self, snapshot_id: str) -> tuple[KnowledgeFragment, ...]: ...


class ConditionCardVerificationError(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    card_id: str
    version: str
    code: str
    detail: str


class ConditionCardAudit(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    snapshot_id: str
    cards_total: int
    verified_cards: tuple[str, ...] = ()
    errors: tuple[ConditionCardVerificationError, ...] = ()
    cards_by_risk: dict[str, int] = Field(default_factory=dict)

    @property
    def passed(self) -> bool:
        return not self.errors and len(self.verified_cards) == self.cards_total


_KNOWN_SLOTS = frozenset(
    {
        "role",
        "process",
        "provider",
        "document_status",
        "duration",
        "duration_minutes",
        "error_code",
        "document_type",
        "model",
        "attempted_action",
        "source_state",
        "category_id",
    }
)


async def _maybe_await(value: Any) -> Any:
    if inspect.isawaitable(value):
        return await value
    return value


async def verify_condition_cards(
    cards: Iterable[ConditionCard],
    repository: ConditionCardRepository,
    snapshot_id: str | None = None,
) -> ConditionCardAudit:
    """Verify provenance and applicability metadata against one snapshot.

    A card is verified only when every referenced fragment is a member of the
    selected normative snapshot and has non-empty source text.  Historical
    snapshots are rejected before any card can become verified.
    """

    values = tuple(cards)
    target_snapshot_id = snapshot_id or (values[0].snapshot_id if values else "")
    errors: list[ConditionCardVerificationError] = []
    snapshot = await _maybe_await(repository.get_snapshot(target_snapshot_id)) if target_snapshot_id else None
    if snapshot is None:
        for card in values:
            errors.append(
                ConditionCardVerificationError(
                    card_id=card.card_id,
                    version=card.version,
                    code="UNKNOWN_SNAPSHOT",
                    detail=f"snapshot {target_snapshot_id!r} does not exist",
                )
            )
        return ConditionCardAudit(
            snapshot_id=target_snapshot_id,
            cards_total=len(values),
            errors=tuple(errors),
            cards_by_risk=dict(Counter(card.risk_level.value for card in values)),
        )

    fragments = await _maybe_await(repository.snapshot_fragments(target_snapshot_id))
    fragment_map = {fragment.fragment_id: fragment for fragment in fragments}
    if snapshot.corpus != Corpus.NORMATIVE:
        for card in values:
            errors.append(
                ConditionCardVerificationError(
                    card_id=card.card_id,
                    version=card.version,
                    code="HISTORICAL_SNAPSHOT_REJECTED",
                    detail=f"cards may only reference normative snapshots, got {snapshot.corpus.value}",
                )
            )

    verified: list[str] = []
    for card in values:
        card_errors: list[ConditionCardVerificationError] = []

        def add(code: str, detail: str) -> None:
            card_errors.append(
                ConditionCardVerificationError(card_id=card.card_id, version=card.version, code=code, detail=detail)
            )

        if card.snapshot_id != target_snapshot_id:
            add("SNAPSHOT_MISMATCH", f"card belongs to {card.snapshot_id}, audit uses {target_snapshot_id}")
        if not card.source_fragment_ids:
            add("MISSING_SOURCE_FRAGMENT", "at least one persisted source fragment is required")
        if not card.source_quotes and not card.source_anchors:
            add("MISSING_PROVENANCE_ANCHOR", "source_quotes or source_anchors must be present")
        if len(card.source_quotes) > len(card.source_fragment_ids):
            add("SOURCE_QUOTE_CARDINALITY", "source quote count cannot exceed source fragment count")
        for slot in card.required_slots:
            if slot not in _KNOWN_SLOTS:
                add("UNKNOWN_REQUIRED_SLOT", f"unsupported required slot {slot!r}")
        if card.risk_level not in tuple(ConditionCardRisk):
            add("INVALID_RISK", f"unsupported risk level {card.risk_level!r}")
        for index, fragment_id in enumerate(card.source_fragment_ids):
            fragment = fragment_map.get(fragment_id)
            if fragment is None:
                add("MISSING_SOURCE_FRAGMENT", f"fragment {fragment_id} is not in normative snapshot {target_snapshot_id}")
                continue
            if fragment.snapshot_id not in (None, target_snapshot_id):
                add("FRAGMENT_SNAPSHOT_MISMATCH", f"fragment {fragment_id} belongs to {fragment.snapshot_id}")
            if not fragment.text.strip():
                add("EMPTY_SOURCE_TEXT", f"fragment {fragment_id} has no source text")
            if index < len(card.source_quotes):
                quote = card.source_quotes[index]
                if quote and quote not in fragment.text:
                    add("SOURCE_QUOTE_MISMATCH", f"quote for fragment {fragment_id} is not an exact source substring")
        if not card_errors and snapshot.corpus == Corpus.NORMATIVE:
            verified.append(f"{card.card_id}@{card.version}")
        errors.extend(card_errors)

    return ConditionCardAudit(
        snapshot_id=target_snapshot_id,
        cards_total=len(values),
        verified_cards=tuple(verified),
        errors=tuple(errors),
        cards_by_risk=dict(Counter(card.risk_level.value for card in values)),
    )
