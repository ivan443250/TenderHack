"""Internal structured output for grounded generation.

The models in this module are deliberately not wire DTOs.  They carry enough
provenance for deterministic verification and are projected to the frozen
``DraftResponse`` only after all checks pass.
"""

from __future__ import annotations

import json
import re
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, ValidationError


class ModelOutputError(ValueError):
    """Raised when a generator does not return the internal JSON shape."""


class GroundedClaim(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    claim_id: str = Field(min_length=1, max_length=128)
    text: str = Field(min_length=1, max_length=8000)
    fragment_ids: tuple[str, ...] = Field(min_length=1)
    evidence_quote: str | None = None
    evidence_reference: str | None = None
    applies_if: tuple[str, ...] = ()
    requires_human_check: bool = False


class GroundedDraft(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    draft_markdown: str = Field(default="", max_length=30000)
    answer_blocks: tuple[str, ...] = ()
    claims: tuple[GroundedClaim, ...] = ()
    missing_slots: tuple[str, ...] = ()
    requires_human_check: bool = False
    # This is an evidence-stage indicator only.  It is never projected to the
    # public contract and cannot contain Support Core decision values.
    evidence_status: str | None = None
    status_indicator: str | None = None
    candidate_status: str | None = None
    decision_candidate: str | None = None


_FORBIDDEN_KEYS = frozenset(
    {
        "decision",
        "should_handoff",
        "handoff_status",
        "resolution_status",
        "reason_codes",
    }
)
_FENCE_RE = re.compile(r"^\s*```(?:json)?\s*(.*?)\s*```\s*$", re.IGNORECASE | re.DOTALL)


def _reject_decision_fields(value: Any, path: str = "output") -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            if str(key).casefold() in _FORBIDDEN_KEYS:
                raise ModelOutputError(f"generator output contains forbidden business field at {path}.{key}")
            _reject_decision_fields(child, f"{path}.{key}")
    elif isinstance(value, list):
        for index, child in enumerate(value):
            _reject_decision_fields(child, f"{path}[{index}]")


def _json_payload(raw: str) -> dict[str, Any]:
    if not isinstance(raw, str) or not raw.strip():
        raise ModelOutputError("generator returned an empty structured draft")
    text = raw.strip()
    fenced = _FENCE_RE.match(text)
    if fenced:
        text = fenced.group(1).strip()
    try:
        value = json.loads(text)
    except json.JSONDecodeError as exc:
        raise ModelOutputError("generator returned invalid JSON") from exc
    if not isinstance(value, dict):
        raise ModelOutputError("structured draft must be a JSON object")
    _reject_decision_fields(value)
    return value


def parse_model_output(raw: str) -> GroundedDraft:
    """Parse and strictly validate one generator response."""

    value = _json_payload(raw)
    # ``list`` is accepted on input for JSON ergonomics; the internal model
    # freezes it to tuples so repeated verification is deterministic.
    try:
        draft = GroundedDraft.model_validate(value)
    except ValidationError as exc:
        raise ModelOutputError("generator structured draft failed schema validation") from exc
    for field_name in ("evidence_status", "status_indicator", "candidate_status", "decision_candidate"):
        field_value = getattr(draft, field_name)
        if field_value is not None and field_value.casefold() in {
            "answer",
            "handoff",
            "handoff_offer",
            "answer_and_handoff",
            "clarify",
            "technical_error",
        }:
            raise ModelOutputError(f"generator {field_name} contains a business decision")
    ids = [claim.claim_id for claim in draft.claims]
    if len(ids) != len(set(ids)):
        raise ModelOutputError("claim_id values must be unique")
    return draft
