"""Deterministic condition-card model and applicability matching."""

from __future__ import annotations

from enum import Enum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class ConditionCardRisk(str, Enum):
    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"


class ConditionCardApplicability(BaseModel):
    """Evidence applicability dimensions, intentionally not business state."""

    model_config = ConfigDict(extra="forbid", frozen=True)

    roles: tuple[str, ...] = ()
    process: tuple[str, ...] = ()
    provider: tuple[str, ...] = ()
    document_status: tuple[str, ...] = ()
    required_conditions: tuple[str, ...] = ()


class ConditionCard(BaseModel):
    """A versioned, reviewable rule about when evidence may be used.

    ``allowed_action`` describes the action documented by the source.  It is
    not a support-core decision enum and must never be interpreted as one.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    card_id: str = Field(min_length=1, max_length=128)
    version: str = Field(min_length=1, max_length=64)
    snapshot_id: str = Field(min_length=1, max_length=128)
    title: str = Field(min_length=1, max_length=300)
    applicability: ConditionCardApplicability
    required_slots: tuple[str, ...] = ()
    allowed_action: str = Field(min_length=1, max_length=2000)
    forbidden_generalization: str = Field(min_length=1, max_length=2000)
    handoff_condition: str = Field(min_length=1, max_length=2000)
    risk_level: ConditionCardRisk
    source_fragment_ids: tuple[str, ...] = Field(min_length=1)
    source_quotes: tuple[str, ...] = ()
    source_anchors: tuple[str, ...] = ()
    review_status: str = Field(default="PENDING_REVIEW", min_length=1, max_length=32)


class CardMatchReason(str, Enum):
    MISSING_REQUIRED_SLOT = "MISSING_REQUIRED_SLOT"
    WRONG_ROLE = "WRONG_ROLE"
    WRONG_PROCESS = "WRONG_PROCESS"
    WRONG_PROVIDER = "WRONG_PROVIDER"
    WRONG_STATUS = "WRONG_STATUS"
    FORBIDDEN_GENERALIZATION = "FORBIDDEN_GENERALIZATION"


class CardMatchContext(BaseModel):
    """Frozen contract context supplied by the caller for card matching."""

    model_config = ConfigDict(extra="forbid", frozen=True)

    role: str | None = None
    process: str | None = None
    provider: str | None = None
    document_status: str | None = None
    duration_minutes: float | None = None
    error_code: str | None = None
    document_type: str | None = None
    model: str | None = None
    attempted_action: str | None = None
    source_state: str | None = None
    category_id: str | None = None
    slots: dict[str, Any] = Field(default_factory=dict)

    def value_for(self, slot: str) -> Any:
        aliases = {
            "duration": "duration_minutes",
            "status": "document_status",
            "role": "role",
            "process": "process",
            "provider": "provider",
            "document_status": "document_status",
            "error_code": "error_code",
            "document_type": "document_type",
            "model": "model",
            "attempted_action": "attempted_action",
            "source_state": "source_state",
            "category_id": "category_id",
        }
        value = self.slots.get(slot)
        if value is not None:
            return value
        attribute = aliases.get(slot, slot)
        return getattr(self, attribute, None)


class CardMatchResult(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    card_id: str
    version: str
    applicable: bool
    score: float = Field(ge=0.0, le=1.0)
    missing_required_slots: tuple[str, ...] = ()
    reasons: tuple[CardMatchReason, ...] = ()


_DIMENSIONS: tuple[tuple[str, str, CardMatchReason], ...] = (
    ("roles", "role", CardMatchReason.WRONG_ROLE),
    ("process", "process", CardMatchReason.WRONG_PROCESS),
    ("provider", "provider", CardMatchReason.WRONG_PROVIDER),
    ("document_status", "document_status", CardMatchReason.WRONG_STATUS),
)


def _same_value(value: Any, allowed: str) -> bool:
    if value is None:
        return False
    return str(value).strip().casefold() == str(allowed).strip().casefold()


def match_card(card: ConditionCard, context: CardMatchContext) -> CardMatchResult:
    """Match a card without semantic inference or hidden defaults.

    An absent applicability value is a missing slot, not a wildcard.  A value
    outside the declared dimension is rejected, preventing a card from being
    generalized to another provider, role, process, or status.
    """

    reasons: list[CardMatchReason] = []
    missing: list[str] = []
    checks = 0
    matched = 0
    for card_field, context_field, mismatch_reason in _DIMENSIONS:
        allowed = getattr(card.applicability, card_field)
        if not allowed:
            continue
        checks += 1
        value = context.value_for(context_field)
        if value is None or str(value).strip() == "":
            missing.append(context_field)
            reasons.append(CardMatchReason.MISSING_REQUIRED_SLOT)
            continue
        if any(_same_value(value, expected) for expected in allowed):
            matched += 1
        else:
            reasons.append(mismatch_reason)
            if card.forbidden_generalization and CardMatchReason.FORBIDDEN_GENERALIZATION not in reasons:
                reasons.append(CardMatchReason.FORBIDDEN_GENERALIZATION)

    for slot in card.required_slots:
        checks += 1
        value = context.value_for(slot)
        if value is None or (isinstance(value, str) and not value.strip()):
            missing.append(slot)
            reasons.append(CardMatchReason.MISSING_REQUIRED_SLOT)
        else:
            matched += 1

    if checks == 0:
        score = 1.0
    else:
        score = matched / checks
    # Keep deterministic reason ordering while avoiding duplicate diagnostics.
    unique_reasons = tuple(dict.fromkeys(reasons))
    unique_missing = tuple(dict.fromkeys(missing))
    applicable = not unique_reasons and not unique_missing
    return CardMatchResult(
        card_id=card.card_id,
        version=card.version,
        applicable=applicable,
        score=score,
        missing_required_slots=unique_missing,
        reasons=unique_reasons,
    )


def card_payload(card: ConditionCard) -> dict[str, Any]:
    """JSON-compatible payload used by both seed tooling and PostgreSQL."""

    return card.model_dump(mode="json")
