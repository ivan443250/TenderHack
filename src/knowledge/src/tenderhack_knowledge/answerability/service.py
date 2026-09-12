"""Deterministic, evidence-driven answerability assessment.

This module assesses evidence only.  It deliberately does not produce a
support-core decision, handoff status, resolution status, or reason code.
"""

from __future__ import annotations

import inspect
import re
import unicodedata
from dataclasses import dataclass
from typing import Any, Protocol

from tenderhack_knowledge.conditions.cards import CardMatchContext, ConditionCard, CardMatchReason, match_card
from tenderhack_knowledge.contracts.v0 import AnswerabilityResponse, EvidenceSufficiency
from tenderhack_knowledge.ingestion.models import Corpus, KnowledgeFragment, KnowledgeSnapshot
from tenderhack_knowledge.ingestion.repository import CorpusBoundaryError, UnknownSnapshotError


class AnswerabilityRepository(Protocol):
    def get_snapshot(self, snapshot_id: str) -> KnowledgeSnapshot | None: ...

    def snapshot_fragments(self, snapshot_id: str) -> tuple[KnowledgeFragment, ...]: ...


@dataclass(frozen=True)
class EvidenceDimensionResult:
    source: bool
    specificity: bool
    applicability: bool
    completeness: bool
    human_need: bool


@dataclass(frozen=True)
class AnswerabilityAssessment:
    evidence_sufficiency: EvidenceSufficiency
    evidence_fragment_ids: tuple[str, ...]
    missing_conditions: tuple[str, ...]
    risk_flags: tuple[str, ...]
    dimensions: EvidenceDimensionResult

    def to_contract(self) -> AnswerabilityResponse:
        return AnswerabilityResponse(
            evidence_sufficiency=self.evidence_sufficiency,
            evidence_fragment_ids=list(self.evidence_fragment_ids),
            missing_conditions=list(self.missing_conditions),
            risk_flags=list(self.risk_flags),
        )


_TOKEN_RE = re.compile(r"[A-Za-z\u0410-\u042f\u0430-\u044f\u0401\u0451][A-Za-z\u0410-\u042f\u0430-\u044f\u0401\u04510-9_-]*|\d+")
_CODE_RE = re.compile(r"(?i)(?:rdik|\u0440\u0434\u0438\u043a)[_-][a-z\u0410-\u042f\u0430-\u044f\u0401\u04510-9_-]+")
_DURATION_RE = re.compile(
    r"(?i)(?P<number>\d+(?:[.,]\d+)?)\s*(?P<unit>\u0441\u0435\u043a\w*|\u043c\u0438\u043d\w*|\u0447\w*|\u0434\w*|\u043c\u0435\u0441\w*)"
)
_WORD_DURATION_RE = re.compile(r"(?i)(?:\u0431\u043e\u043b\u044c\u0448\u0435|\u0431\u043e\u043b\u0435\u0435|\u043f\u0440\u043e\u0448\u043b\w*)\s+(?:\u043e\u0434\u043d\u043e\u0433\u043e\s+)?\u0447\u0430\u0441")
_ALREADY_TRIED_RE = re.compile(
    r"(?i)(?:\u0443\u0436\u0435|\u043f\u0440\u043e\u0431\u043e\u0432\w*|\u0441\u0434\u0435\u043b\u0430\u043b\w*|\u0437\u0430\u0433\u0440\u0443\u0437\u0438\u043b\w*|\u043d\u0435\s+\u043f\u043e\u043c\u043e\u0433\u043b\w*|\u043d\u0435\s+\u043f\u043e\u043b\u0443\u0447\u0430\u0435\u0442\u0441\u044f)"
)
_NEGATED_ASSERTION_RE = re.compile(r"(?i)(?:\u043d\u0435\s+\u043d\u0443\u0436\u043d\w*|\u043d\u0435\s+\u0442\u0440\u0435\u0431\u0443\u0435\u0442\u0441\u044f|\u043d\u0435\u043b\u044c\u0437\u044f|\u043d\u0435\u0432\u043e\u0437\u043c\u043e\u0436\u043d\w*)")
r"""
_SUPPORT_RE = re.compile(
    r"(?i)(?:\u043e\u0431\u0440\u0430\u0442\u0438\u0442\u0435\u0441\u044c|\u043e\u0431\u0440\u0430\u0449\u0430\u0442\u044c\u0441\u044f|\u0441\u0442\u043f\s*\u043f\b|\u0442\u0435\u0445\u043d\u0438\u0447\u0435\u0441\uк\w*\s+\u043f\u043e\u0434\u0434\u0435\u0440\u0436\u043a\w*)"
)
"""
_SUPPORT_RE = re.compile(
    r"(?i)(?:\u043e\u0431\u0440\u0430\u0442\u0438\u0442\u0435\u0441\u044c|\u043e\u0431\u0440\u0430\u0449\u0430\u0442\u044c\u0441\u044f|\u0441\u0442\u043f\s*\u043f\b|\u0442\u0435\u0445\u043d\u0438\u0447\u0435\u0441\u043a\w*\s+\u043f\u043e\u0434\u0434\u0435\u0440\u0436\u043a\w*)"
)

_STOPWORDS = frozenset(
    {
        "\u0430", "\u0431\u044b", "\u0432", "\u0432\u043e", "\u0434\u0430", "\u0434\u043b\u044f", "\u0436\u0435", "\u0437\u0430", "\u0438", "\u0438\u0437", "\u043a", "\u043a\u0430\u043a", "\u043a\u043e", "\u043b\u0438", "\u043c\u043e\u0439", "\u043c\u043e\u044f", "\u043d\u0430", "\u043d\u0435", "\u043d\u043e", "\u043e", "\u043e\u0431", "\u043e\u043d", "\u043f\u043e", "\u043f\u043e\u0434", "\u043f\u0440\u0438", "\u0441", "\u0441\u043e", "\u0442\u0430\u043a", "\u0442\u043e", "\u0443", "\u0438\u043b\u0438", "\u0447\u0442\u043e", "\u044d\u0442\u043e", "\u044f", "\u043c\u043d\u0435",
        "\u043d\u0443\u0436\u043d\u043e", "\u043c\u043e\u0436\u043d\u043e", "\u043f\u043e\u0436\u0430\u043b\u0443\u0439\u0441\u0442\u0430", "\u043f\u043e\u0434\u0441\u043a\u0430\u0436\u0438\u0442\u0435", "\u043f\u043e\u043c\u043e\u0433\u0438\u0442\u0435", "\u043f\u043e\u043a\u0430\u0436\u0438\u0442\u0435", "\u043f\u043e\u0447\u0435\u043c\u0443", "\u0433\u0434\u0435", "\u0443\u0436\u0435", "\u0441\u0434\u0435\u043b\u0430\u043b", "\u0441\u0434\u0435\u043b\u0430\u0442\u044c", "\u043f\u0440\u043e\u0431\u043e\u0432\u0430\u043b", "\u043f\u0440\u043e\u0431\u043e\u0432\u0430\u043b\u0430", "\u043d\u0435\u043f\u043e\u043b\u0443\u0447\u0430\u0435\u0442\u0441\u044f",
    }
)
_GENERIC_TOKENS = frozenset({"\u0441\u0442\u0430\u0442\u0443\u0441", "\u0434\u043e\u043a\u0443\u043c\u0435\u043d\u0442", "\u043e\u0448\u0438\u0431\u043a\u0430", "\u043f\u0440\u043e\u0431\u043b\u0435\u043c\u0430", "\u043f\u043e\u0440\u0442\u0430\u043b", "\u043f\u043e\u0434\u0434\u0435\u0440\u0436\u043a\u0430"})


def _normalize(value: str) -> str:
    try:
        repaired = value.encode("cp1251").decode("utf-8")
        if repaired and repaired != value:
            value = repaired
    except (UnicodeEncodeError, UnicodeDecodeError):
        pass
    # A few legacy card rows contain UTF-8 bytes decoded once as cp1251
    # (``Рї...``). Repair only that recognizable marker; ordinary Russian text
    # is left untouched.
    if "Р" in value or "С" in value:
        try:
            repaired = value.encode("cp1251").decode("utf-8")
            if repaired:
                value = repaired
        except (UnicodeEncodeError, UnicodeDecodeError):
            pass
    normalized = unicodedata.normalize("NFC", value).replace("\u0451", "\u0435").replace("\u0401", "\u0415").casefold()
    # User text commonly uses Cyrillic abbreviations while source metadata
    # uses their Latin contract spellings.
    return normalized.replace("\u0443\u043f\u0434", "upd").replace("\u0443\u043a\u0434", "ukd").replace("\u043c\u0447\u0434", "mcd")


def _tokens(value: str) -> tuple[str, ...]:
    return tuple(_normalize(match.group(0)) for match in _TOKEN_RE.finditer(value))


def _content_tokens(value: str) -> tuple[str, ...]:
    return tuple(token for token in _tokens(value) if token not in _STOPWORDS and (len(token) >= 3 or token.isdigit()))


def _stem(token: str) -> str:
    if token.isascii() or token.isdigit() or len(token) < 6:
        return token
    return token[:5]


def _parse_duration_minutes(query: str) -> float | None:
    match = _DURATION_RE.search(query)
    if match:
        number = float(match.group("number").replace(",", "."))
        unit = _normalize(match.group("unit"))
        if unit.startswith("\u0441\u0435\u043a"):
            return number / 60
        if unit.startswith("\u043c\u0438\u043d"):
            return number
        if unit.startswith("\u0447"):
            return number * 60
        if unit.startswith("\u0434"):
            return number * 1440
        return number * 43200
    if _WORD_DURATION_RE.search(query):
        return 61.0
    return None


def _context(query: str, cards: tuple[ConditionCard, ...]) -> CardMatchContext:
    normalized = _normalize(query)
    process: str | None = None
    if re.search(r"(?i)\u0438\u0441\u043f\u0440\u0430\u0432\u0438\u0442\w*.*(?:\u0443\u043f\u0434|\u0443\u043a\u0434)|(?:\u0443\u043f\u0434|\u0443\u043a\u0434).*\u0438\u0441\u043f\u0440\u0430\u0432\u0438\u0442", query):
        process = "UPD correction"
    elif re.search(r"(?i)\u0443\u043f\u0434|\u0443\u043a\u0434|\u0438\u0441\u043f\u043e\u043b\u043d\u0435\u043d", query):
        process = "contract execution" if re.search(r"(?i)\u0442\u0438\u043f\w*\s+\u0434\u043e\u043a\u0443\u043c\u0435\u043d\u0442|\u0434\u043e\u0431\u0430\u0432\u043b\u0435\u043d\u0438\u0435\s+\u0438\u0441\u043f\u043e\u043b\u043d\u0435\u043d", query) else "UPD execution"
    elif re.search(r"(?i)\u043c\u0447\u0434|\u0434\u043e\u0432\u0435\u0440\u0435\u043d", query):
        process = "MCD management"
    elif re.search(r"(?i)yml|\u043f\u0440\u0430\u0439\u0441", query):
        process = "YML import"
    elif re.search(r"(?i)ste|\u0441\u0442\u0435|\u043e\u0444\u0435\u0440\u0442|\u0438\u043c\u043f\u043e\u0440\u0442\w*\s+\u0444\u0430\u0439\w*", query):
        process = "offer import" if re.search(r"(?i)\u0438\u043c\u043f\u043e\u0440\u0442|\u0437\u0430\u0433\u0440\u0443\u0437\u043a|\u0441\u0442\u0430\u0442\u0443\u0441", query) else "offer/STE"

    if re.search(r"(?i)\u0430\u0434\u043c\u0438\u043d\u0438\u0441\u0442\u0440\u0430\u0442\w*", query):
        role = "company administrator"
    elif process == "MCD management":
        if re.search(r"(?i)\u0430\u0434\u043c\u0438\u043d\u0438\u0441\u0442\u0440\u0430\u0442\w*", query):
            role = "company administrator"
        elif re.search(r"(?i)\u043f\u043e\u043b\u044c\u0437\u043e\u0432\u0430\u0442\w*|\u043f\u043e\u0441\u0442\u0430\u0432\u0449\u0438\u043a", query):
            role = "supplier user"
        else:
            role = None
    elif re.search(r"(?i)\u043f\u043e\u0441\u0442\u0430\u0432\u0449\u0438\u043a", query):
        role = "supplier"
    elif re.search(r"(?i)\u0437\u0430\u043a\u0430\u0437\u0447\u0438\u043a", query):
        role = "customer"
    else:
        role = None

    if re.search(r"(?i)\u0435\u0438\u0441|\u044d\u0438\u0441", query):
        provider = "EIS"
    elif re.search(r"(?i)\u043f\u043e\u0440\u0442\u0430\u043b\w*\s+\u043f\u043e\u0441\u0442\u0430\u0432\u0449\u0438\u043a", query):
        provider = "Supplier Portal"
    elif re.search(r"(?i)\u043f\u043e\u0440\u0442\u0430\u043b\w*\s+\u0437\u0430\u043a\u0430\u0437\u0447\u0438\u043a", query):
        provider = "Customer Portal"
    else:
        provider = None

    status: str | None = None
    for card in cards:
        for candidate in card.applicability.document_status:
            if _normalize(candidate) in normalized:
                status = candidate
                break
        if status is not None:
            break
    # A generic status mention is still evidence that the caller supplied a
    # status slot. It must not satisfy cards with a declared, incompatible
    # status value (those will be rejected by match_card).
    if status is None and re.search(r"(?i)\u0441\u0442\u0430\u0442\u0443\u0441\w*|\u0441\u043e\u0441\u0442\u043e\u044f\u043d\u0438\u0435", query):
        status = "present"

    code_match = _CODE_RE.search(query)
    error_code = code_match.group(0) if code_match else None
    if error_code is None:
        for token in _tokens(query):
            if token.upper().startswith(("RDIK", "\u0420\u0414\u0418\u041a")):
                error_code = token
                break
    document_type = None
    document_terms = (
        ("UPD", ("UPD", "\u0443\u043f\u0434")),
        ("UKD", ("UKD", "\u0443\u043a\u0434")),
        ("MCD", ("MCD", "\u043c\u0447\u0434")),
        ("YML", ("YML",)),
        ("STE", ("STE", "\u0441\u0442\u0435")),
    )
    for candidate, terms in document_terms:
        if any(_normalize(term) in normalized for term in terms):
            document_type = candidate
            break
    if document_type is None and re.search(r"(?i)\u0443\u043a\u0434", query):
        document_type = "UKD"
    duration = _parse_duration_minutes(query)
    attempted = "already tried" if _ALREADY_TRIED_RE.search(query) else None
    source_state = status
    category_id = None
    if re.search(r"(?i)\u043a\u0430\u0442\u0435\u0433\u043e\u0440\w*|category|ppcategory", query):
        category_id = next((token for token in _tokens(query) if token.isdigit()), None)
    model = "present" if re.search(r"(?i)\u043c\u043e\u0434\u0435\u043b\w*", query) else None
    return CardMatchContext(
        role=role,
        process=process,
        provider=provider,
        document_status=status,
        duration_minutes=duration,
        error_code=error_code,
        document_type=document_type,
        attempted_action=attempted,
        source_state=source_state,
        category_id=category_id,
        model=model,
    )


def _specificity(query: str, fragments: tuple[KnowledgeFragment, ...]) -> tuple[bool, float]:
    query_tokens = _content_tokens(query)
    if not query_tokens:
        return False, 0.0
    source_tokens_raw = {token for fragment in fragments for token in _content_tokens(fragment.text)}
    source_tokens = {_stem(token) for token in source_tokens_raw}
    exact_codes = {_normalize(code) for code in _CODE_RE.findall(query)}
    exact_code_hit = bool(exact_codes) and exact_codes.issubset(source_tokens_raw)
    overlap = {_stem(token) for token in query_tokens} & source_tokens
    ratio = len(overlap) / max(len({_stem(token) for token in query_tokens}), 1)
    if exact_code_hit:
        return True, 1.0
    if len(query_tokens) == 1 and query_tokens[0] in _GENERIC_TOKENS:
        return False, ratio
    # One distinctive long term is enough; otherwise require two concepts or
    # a strong overlap.  No retrieval score or top-1/top-2 gap is consulted.
    distinctive = any(len(token) >= 7 and _stem(token) in overlap for token in query_tokens)
    addressed = (len(overlap) >= 2 and ratio >= 0.25) or (distinctive and ratio >= 0.2)
    return addressed, ratio


def _contradictory(fragments: tuple[KnowledgeFragment, ...]) -> bool:
    if len(fragments) < 2:
        return False
    texts = [_normalize(fragment.text) for fragment in fragments]
    positive_required = any("\u043e\u0431\u044f\u0437\u0430\u0442\u0435\u043b\u044c\u043d" in text for text in texts)
    negative_required = any(
        "\u043d\u0435\u043e\u0431\u044f\u0437\u0430\u0442\u0435\u043b\u044c\u043d" in text
        or re.search(r"\u043d\u0435\s+\u043e\u0431\u044f\u0437\u0430\u0442\u0435\u043b\w*", text)
        or "\u043d\u0435 \u0442\u0440\u0435\u0431\u0443\u0435\u0442\u0441\u044f" in text
        for text in texts
    )
    positive_permission = any("\u043c\u043e\u0436\u043d\u043e" in text for text in texts)
    negative_permission = any("\u043d\u0435\u043b\u044c\u0437\u044f" in text or "\u043d\u0435 \u043c\u043e\u0436\u043d\u043e" in text for text in texts)
    return (positive_required and negative_required) or (positive_permission and negative_permission)


async def _maybe_await(value: Any) -> Any:
    return await value if inspect.isawaitable(value) else value


async def _load_cards(repository: Any, snapshot_id: str) -> tuple[ConditionCard, ...]:
    method = getattr(repository, "condition_cards", None)
    if not callable(method):
        return ()
    try:
        values = await _maybe_await(method(snapshot_id))
    except (AttributeError, NotImplementedError):
        return ()
    return _latest_cards(tuple(values or ()))


def _latest_cards(cards: tuple[ConditionCard, ...]) -> tuple[ConditionCard, ...]:
    """Use one immutable version per card id for a snapshot-aware decision.

    Historical versions remain persisted for audit, but evaluating all
    versions together would let a superseded card add stale required slots.
    Numeric versions sort naturally; non-numeric versions use a stable lexical
    fallback.
    """

    def version_key(version: str) -> tuple[int, object]:
        try:
            return (1, int(version))
        except ValueError:
            return (0, version)

    latest: dict[str, ConditionCard] = {}
    for card in cards:
        previous = latest.get(card.card_id)
        if previous is None or version_key(card.version) > version_key(previous.version):
            latest[card.card_id] = card
    return tuple(latest[key] for key in sorted(latest))


async def assess_answerability(
    query: str,
    snapshot_id: str,
    candidate_fragment_ids: tuple[str, ...] | list[str],
    repository: AnswerabilityRepository,
    *,
    cards: tuple[ConditionCard, ...] | list[ConditionCard] | None = None,
) -> AnswerabilityAssessment:
    """Assess five evidence dimensions using only the frozen request context."""

    snapshot = await _maybe_await(repository.get_snapshot(snapshot_id))
    if snapshot is None:
        raise UnknownSnapshotError(snapshot_id)
    if snapshot.corpus != Corpus.NORMATIVE:
        raise CorpusBoundaryError(f"answerability requires a normative snapshot, got {snapshot.corpus.value}")
    fragments = tuple(await _maybe_await(repository.snapshot_fragments(snapshot_id)))
    by_id = {fragment.fragment_id: fragment for fragment in fragments}
    requested_ids = tuple(dict.fromkeys(str(value) for value in candidate_fragment_ids))
    invalid_ids = tuple(fragment_id for fragment_id in requested_ids if fragment_id not in by_id)
    evidence = tuple(fragment for fragment_id in requested_ids if (fragment := by_id.get(fragment_id)) is not None and fragment.text.strip())

    risk_flags: list[str] = []
    missing: list[str] = []
    if invalid_ids:
        risk_flags.append("INVALID_EVIDENCE_REFERENCE")
    if not evidence:
        risk_flags.append("MISSING_SOURCE")
        if invalid_ids:
            missing.append("candidate fragments must belong to the requested normative snapshot")
        return AnswerabilityAssessment(
            evidence_sufficiency=EvidenceSufficiency.INSUFFICIENT,
            evidence_fragment_ids=(),
            missing_conditions=tuple(missing),
            risk_flags=tuple(dict.fromkeys(risk_flags)),
            dimensions=EvidenceDimensionResult(False, False, False, False, False),
        )
    if invalid_ids:
        # Do not answer from a partially unverifiable candidate set. Returning
        # the valid IDs keeps diagnostics useful while the gate remains
        # conservative.
        missing.append("candidate fragments must belong to the requested normative snapshot")
        return AnswerabilityAssessment(
            evidence_sufficiency=EvidenceSufficiency.INSUFFICIENT,
            evidence_fragment_ids=tuple(fragment.fragment_id for fragment in evidence),
            missing_conditions=tuple(missing),
            risk_flags=("INVALID_EVIDENCE_REFERENCE",),
            dimensions=EvidenceDimensionResult(True, False, False, False, False),
        )

    all_cards = _latest_cards(tuple(cards)) if cards is not None else await _load_cards(repository, snapshot_id)
    context = _context(query, all_cards)
    linked_cards = tuple(
        card for card in all_cards if set(card.source_fragment_ids).intersection(fragment.fragment_id for fragment in evidence)
    )
    card_matches = tuple((card, match_card(card, context)) for card in linked_cards)
    applicable_cards = tuple(card for card, result in card_matches if result.applicable)
    mismatched_cards = tuple((card, result) for card, result in card_matches if not result.applicable)
    for _card, result in mismatched_cards:
        for slot in result.missing_required_slots:
            if slot not in missing:
                missing.append(slot)
        if CardMatchReason.WRONG_PROVIDER in result.reasons:
            risk_flags.append("WRONG_PROVIDER")
        if CardMatchReason.WRONG_STATUS in result.reasons:
            risk_flags.append("WRONG_STATUS")
        if CardMatchReason.WRONG_ROLE in result.reasons:
            risk_flags.append("WRONG_ROLE")
        if CardMatchReason.FORBIDDEN_GENERALIZATION in result.reasons:
            risk_flags.append("FORBIDDEN_GENERALIZATION")
        if result.missing_required_slots and _card.risk_level.value == "HIGH":
            risk_flags.append("HIGH_RISK_MISSING_CONDITION")

    specificity_ok, specificity_ratio = _specificity(query, evidence)
    if _NEGATED_ASSERTION_RE.search(query):
        negative_evidence = any(
            re.search(r"(?i)\u043d\u0435\s+\u043d\u0443\u0436\u043d\w*|\u043d\u0435\s+\u0442\u0440\u0435\u0431\u0443\u0435\u0442\u0441\u044f|\u043d\u0435\u043b\u044c\u0437\u044f|\u043d\u0435\u0432\u043e\u0437\u043c\u043e\u0436\u043d\w*", _normalize(fragment.text))
            for fragment in evidence
        )
        if not negative_evidence:
            specificity_ok = False
            risk_flags.append("NEGATED_QUERY")
    if not specificity_ok:
        risk_flags.append("LOW_SPECIFICITY")
    human_need = bool(_ALREADY_TRIED_RE.search(query))
    if human_need:
        risk_flags.append("POST_INSTRUCTION_FAILURE")
    source_support_fragments = tuple(fragment for fragment in evidence if _SUPPORT_RE.search(fragment.text))
    # Some manuals mention support only on a threshold branch (for example,
    # status unchanged for more than one hour). The mention alone must not
    # force a handoff; the threshold belongs to the caller's applicability
    # context. Unconditional outage/support instructions remain human-needed.
    conditional_support = any(
        "\u0431\u043e\u043b\u0435\u0435 \u0447\u0430\u0441" in _normalize(fragment.text)
        and "\u0435\u0441\u043b\u0438" in _normalize(fragment.text)
        for fragment in source_support_fragments
    )
    query_explicit_support = bool(_SUPPORT_RE.search(query))
    source_requires_support = bool(source_support_fragments) and (
        not conditional_support or query_explicit_support or (context.duration_minutes is not None and context.duration_minutes > 60)
    )
    if source_requires_support:
        human_need = True
        risk_flags.append("HUMAN_SUPPORT_REQUIRED")
    if any(card.handoff_condition and _SUPPORT_RE.search(card.handoff_condition) for card in applicable_cards):
        human_need = True
        risk_flags.append("HUMAN_SUPPORT_REQUIRED")

    role_markers = set()
    for fragment in evidence:
        text = _normalize(fragment.text)
        if "\u043f\u043e\u0441\u0442\u0430\u0432\u0449\u0438\u043a" in text:
            role_markers.add("supplier")
        if "\u0437\u0430\u043a\u0430\u0437\u0447\u0438\u043a" in text:
            role_markers.add("customer")
    role_ambiguous = len(role_markers) > 1 and context.role is None
    if role_ambiguous:
        risk_flags.append("ROLE_AMBIGUITY")
    conflict = _contradictory(evidence)
    if conflict:
        risk_flags.append("CONFLICTING_EVIDENCE")

    # Missing context is a completeness gap; an explicit mismatch is an
    # applicability failure. This distinction leaves one useful clarification
    # path instead of treating an underspecified query as wrong.
    wrong_applicability_reasons = {
        CardMatchReason.WRONG_PROVIDER,
        CardMatchReason.WRONG_STATUS,
        CardMatchReason.WRONG_ROLE,
        CardMatchReason.WRONG_PROCESS,
        CardMatchReason.FORBIDDEN_GENERALIZATION,
    }
    applicability_mismatch = any(
        any(reason in wrong_applicability_reasons for reason in result.reasons)
        for _card, result in mismatched_cards
    )
    applicability_ok = not applicability_mismatch and not role_ambiguous
    completeness_ok = not missing
    source_ok = bool(evidence)
    dimensions = EvidenceDimensionResult(source_ok, specificity_ok, applicability_ok, completeness_ok, human_need)

    if human_need or conflict or not specificity_ok or not applicability_ok:
        sufficiency = EvidenceSufficiency.INSUFFICIENT
    elif not completeness_ok:
        sufficiency = EvidenceSufficiency.CONDITION_DEPENDENT
    elif applicable_cards or specificity_ratio >= 0.2:
        sufficiency = EvidenceSufficiency.SUFFICIENT
    else:
        sufficiency = EvidenceSufficiency.INSUFFICIENT

    # The IDs are the only evidence references emitted, in caller order.
    evidence_ids = tuple(fragment.fragment_id for fragment in evidence)
    return AnswerabilityAssessment(
        evidence_sufficiency=sufficiency,
        evidence_fragment_ids=evidence_ids,
        missing_conditions=tuple(dict.fromkeys(missing)),
        risk_flags=tuple(dict.fromkeys(risk_flags)),
        dimensions=dimensions,
    )
