"""Deterministic claim-level source verification."""

from __future__ import annotations

import inspect
import re
import unicodedata
from dataclasses import dataclass
from typing import Any

from tenderhack_knowledge.contracts.v0 import Claim, VerifyResult
from tenderhack_knowledge.ingestion.models import Corpus, KnowledgeFragment
from tenderhack_knowledge.ingestion.repository import CorpusBoundaryError, UnknownSnapshotError


@dataclass(frozen=True)
class ClaimVerification:
    result: VerifyResult
    reasons: tuple[str, ...] = ()


class VerificationRepository:
    async def get_snapshot(self, snapshot_id: str) -> Any: ...

    async def snapshot_fragments(self, snapshot_id: str) -> tuple[KnowledgeFragment, ...]: ...


_TOKEN_RE = re.compile(r"[A-Za-z\u0410-\u042f\u0430-\u044f\u0401\u0451][A-Za-z\u0410-\u042f\u0430-\u044f\u0401\u04510-9_-]*|\d+(?:[.,]\d+)?")
_URL_RE = re.compile(r"https?://[^\s)]+", re.IGNORECASE)
_CODE_RE = re.compile(r"(?i)(?:rdik|\u0440\u0434\u0438\u043a)[_-][a-z\u0430-\u044f\u04510-9_-]+")
_QUOTED_RE = re.compile(r"[\u00ab\"']([^\u00bb\"']{2,120})[\u00bb\"']")
_BUTTON_RE = re.compile(r"(?i)(?:button|\u043a\u043d\u043e\u043f\w*)\s+([A-Za-z\u0410-\u042f\u0430-\u044f\u0401\u0451][A-Za-z\u0410-\u042f\u0430-\u044f\u0401\u04510-9 _-]{1,80})")
_STATUS_RE = re.compile(r"(?i)\b(?:status|\u0441\u0442\u0430\u0442\u0443\u0441)\b")
_STOPWORDS = frozenset(
    "\u0430 \u0431\u0435\u0437 \u0431\u043e\u043b\u0435\u0435 \u0431\u044b \u0431\u044b\u043b \u0431\u044b\u0442\u044c \u0432 \u0432\u043e \u0432\u043e\u0442 \u0432\u044b \u0434\u0430 \u0434\u043b\u044f \u0434\u043e \u0435\u0441\u043b\u0438 \u0438 \u0438\u0437 \u043a \u043a\u0430\u043a \u043a\u043e \u043b\u0438 \u043b\u0438\u0431\u043e \u043c\u043d\u0435 \u043c\u044b \u043d\u0430 \u043d\u0430\u0434 \u043d\u0435 \u043d\u0435\u0442 \u043d\u043e \u043e \u043e\u0431 \u043e\u0442 \u043f\u043e \u043f\u043e\u0434 \u043f\u0440\u0438 \u0441 \u0441\u043e \u0442\u0430\u043a \u0442\u043e \u0443 \u0443\u0436\u0435 \u0447\u0442\u043e \u044d\u0442\u043e \u044f \u043d\u0443\u0436\u043d\u043e \u043c\u043e\u0436\u043d\u043e \u043f\u043e\u0436\u0430\u043b\u0443\u0439\u0441\u0442\u0430 \u043a\u0430\u043a \u0433\u0434\u0435 \u043f\u043e\u0447\u0435\u043c\u0443 \u043f\u043e\u043c\u043e\u0433\u0438\u0442\u0435 \u043f\u043e\u043a\u0430\u0436\u0438\u0442\u0435".split()
)


def _normalize(value: str) -> str:
    return unicodedata.normalize("NFC", value).replace("\u0451", "\u0435").replace("\u0401", "\u0415").casefold()


def _tokens(value: str) -> tuple[str, ...]:
    return tuple(_normalize(match.group(0)) for match in _TOKEN_RE.finditer(value))


def _stem(token: str) -> str:
    if token.isascii() or token.isdigit() or len(token) < 6:
        return token
    return token[:5]


def _content_tokens(value: str) -> tuple[str, ...]:
    return tuple(token for token in _tokens(value) if token not in _STOPWORDS and (len(token) >= 3 or token.replace(".", "", 1).isdigit()))


def _numbers(value: str) -> frozenset[str]:
    return frozenset(match.group(0).replace(",", ".") for match in re.finditer(r"(?<![A-Za-z\u0410-\u042f\u0430-\u044f\u0401\u0451])\d+(?:[.,]\d+)?", value))


def _codes(value: str) -> frozenset[str]:
    return frozenset(_normalize(match.group(0)) for match in _CODE_RE.finditer(value))


def _urls(value: str) -> frozenset[str]:
    return frozenset(match.group(0).rstrip(".,;:)") for match in _URL_RE.finditer(value))


def _quoted(value: str) -> tuple[str, ...]:
    return tuple(_normalize(match.group(1).strip()) for match in _QUOTED_RE.finditer(value))


def _button_labels(value: str) -> tuple[str, ...]:
    labels: list[str] = []
    for match in _BUTTON_RE.finditer(value):
        label = match.group(1).strip().rstrip(".,;:)")
        if label:
            labels.append(_normalize(label))
    return tuple(labels)


def _status_terms(value: str) -> tuple[str, ...]:
    marker = _STATUS_RE.search(value)
    if marker is None:
        return ()
    tail = value[marker.end() :]
    terms = _content_tokens(tail)
    return tuple(term for term in terms if term not in {"upd", "ukd", "mcd", "document", "\u0434\u043e\u043a\u0443\u043c\u0435\u043d\u0442"})


def _contains_phrase(phrase: str, evidence: str) -> bool:
    return _normalize(phrase).strip() in _normalize(evidence)


def _verify_one(claim: Any, fragments: dict[str, KnowledgeFragment]) -> ClaimVerification:
    if not claim.fragment_ids:
        return ClaimVerification(VerifyResult(claim_id=claim.claim_id, supported=False, evidence_fragment_ids=[]), ("NO_EVIDENCE_REFERENCE",))
    missing = tuple(fragment_id for fragment_id in claim.fragment_ids if fragment_id not in fragments)
    if missing:
        return ClaimVerification(VerifyResult(claim_id=claim.claim_id, supported=False, evidence_fragment_ids=[]), ("WRONG_SNAPSHOT_FRAGMENT",))
    selected = tuple(fragments[fragment_id] for fragment_id in dict.fromkeys(claim.fragment_ids))
    evidence = "\n".join(fragment.text for fragment in selected if fragment.text.strip())
    if not evidence.strip():
        return ClaimVerification(VerifyResult(claim_id=claim.claim_id, supported=False, evidence_fragment_ids=[]), ("EMPTY_EVIDENCE",))

    reasons: list[str] = []
    claim_numbers = _numbers(claim.text)
    if not claim_numbers.issubset(_numbers(evidence)):
        reasons.append("INVENTED_NUMBER")
    claim_codes = _codes(claim.text)
    if not claim_codes.issubset(_codes(evidence)):
        reasons.append("INVENTED_CODE")
    if not _urls(claim.text).issubset(_urls(evidence)):
        reasons.append("UNKNOWN_URL")
    if any(not _contains_phrase(phrase, evidence) for phrase in _quoted(claim.text)):
        reasons.append("INVENTED_ACTION_OR_STATUS")
    if any(not _contains_phrase(label, evidence) for label in _button_labels(claim.text)):
        reasons.append("INVENTED_ACTION_OR_STATUS")
    status_terms = _status_terms(claim.text)
    if status_terms and not all(_contains_phrase(term, evidence) for term in status_terms):
        reasons.append("INVENTED_ACTION_OR_STATUS")
    for condition in getattr(claim, "applies_if", ()):
        if not _contains_phrase(condition, evidence):
            reasons.append("APPLICABILITY_DROPPED")
    evidence_quote = getattr(claim, "evidence_quote", None)
    evidence_reference = getattr(claim, "evidence_reference", None)
    if evidence_quote and not _contains_phrase(evidence_quote, evidence):
        reasons.append("QUOTE_NOT_IN_EVIDENCE")
    if evidence_reference and not _contains_phrase(evidence_reference, evidence):
        reasons.append("REFERENCE_NOT_IN_EVIDENCE")

    claim_tokens = {_stem(token) for token in _content_tokens(claim.text)}
    evidence_tokens = {_stem(token) for token in _content_tokens(evidence)}
    overlap = claim_tokens & evidence_tokens
    generic = {"document", "status", "error", "portal", "section", "\u0434\u043e\u043a\u0443\u043c\u0435\u043d\u0442", "\u0441\u0442\u0430\u0442\u0443\u0441", "\u043e\u0448\u0438\u0431\u043a\u0430", "\u043f\u043e\u0440\u0442\u0430\u043b", "\u0440\u0430\u0437\u0434\u0435\u043b"}
    distinctive_overlap = {token for token in overlap if token not in generic}
    if not claim_codes and not claim_numbers:
        if not distinctive_overlap:
            reasons.append("LOW_TEXT_GROUNDING")
    elif claim_tokens and len(overlap) / max(len(claim_tokens), 1) < 0.2 and not claim_codes:
        reasons.append("LOW_TEXT_GROUNDING")
    supported = not reasons
    return ClaimVerification(
        VerifyResult(
            claim_id=claim.claim_id,
            supported=supported,
            evidence_fragment_ids=list(claim.fragment_ids) if supported else [],
        ),
        tuple(dict.fromkeys(reasons)),
    )


def draft_text_issues(markdown: str, evidence: tuple[str, ...] | list[str]) -> tuple[str, ...]:
    """Check unsafe source links in rendered text before public projection.

    Claim-level checks carry the factual guarantees; this extra inexpensive
    check prevents a model from adding an ungrounded URL outside claim text.
    """

    known = _urls("\n".join(evidence))
    if not _urls(markdown).issubset(known):
        return ("UNKNOWN_URL",)
    return ()


async def verify_claims(
    snapshot_id: str,
    claims: list[Claim] | tuple[Claim, ...] | list[Any] | tuple[Any, ...],
    repository: VerificationRepository,
) -> tuple[ClaimVerification, ...]:
    snapshot = repository.get_snapshot(snapshot_id)
    if inspect.isawaitable(snapshot):
        snapshot = await snapshot
    if snapshot is None:
        raise UnknownSnapshotError(snapshot_id)
    if snapshot.corpus not in {Corpus.NORMATIVE, Corpus.HISTORICAL}:
        raise CorpusBoundaryError(f"verification does not support snapshot corpus {snapshot.corpus.value}")
    values = repository.snapshot_fragments(snapshot_id)
    if inspect.isawaitable(values):
        values = await values
    fragments = {fragment.fragment_id: fragment for fragment in values}
    if snapshot.corpus == Corpus.HISTORICAL and any(
        fragment.kind != "HISTORICAL_SUPPORT_SOLUTION" or fragment.review_status != "VERIFIED"
        for fragment in fragments.values()
    ):
        raise CorpusBoundaryError("historical verification requires verified support-solution evidence")
    return tuple(_verify_one(claim, fragments) for claim in claims)
