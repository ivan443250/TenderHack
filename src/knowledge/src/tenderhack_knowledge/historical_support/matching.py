"""Conservative, explainable matching for verified historical solutions."""

from __future__ import annotations

import re
import unicodedata
from dataclasses import dataclass
from difflib import SequenceMatcher
from typing import Iterable

from tenderhack_knowledge.ingestion.models import KnowledgeFragment


_TOKEN_RE = re.compile(r"[a-zа-яё0-9]+", re.IGNORECASE)
_PREFIX_RE = re.compile(r"^\s*подтема\s+запроса\s*:\s*", re.IGNORECASE)
_STOPWORDS = frozenset(
    "а без более бы был была были в во для до его ее их из или как мне мой моя на над не нет но о об от по под при с со так у уже что это я нужно можно пожалуйста подскажите помогите покажите здравствуйте".split()
)
_DOMAIN_PREFIXES = (
    "portal", "портал", "postav", "постав", "zakaz", "заказ", "upd", "ukd", "ste", "mcd", "yml", "edo",
    "оферт", "контракт", "закуп", "исполн", "документ", "специф", "позици", "калькул", "каталог", "импорт",
    "рдик", "rdik", "доверен", "личн", "кабинет", "подпис", "статус",
)
_ACTION_PREFIXES = (
    "созда", "загруз", "добав", "удал", "измен", "сохран", "подпис", "отправ", "импорт", "пересч", "оформ",
    "выбер", "нажм", "откры", "перей", "исправ", "обнов", "сформир", "укаж", "прикреп", "аннулир",
)
_WEAK_SOLUTIONS = re.compile(r"(?i)^\s*(?:исправлено|решено|готово|обратитесь\s+в\s+стп|требуется\s+уточнение)[.!\s]*$")


@dataclass(frozen=True)
class HistoricalMatch:
    fragment: KnowledgeFragment
    score: float
    strong: bool
    domain_overlap: tuple[str, ...]
    action_overlap: tuple[str, ...]
    contradiction: bool


def normalize_historical_text(value: str) -> str:
    normalized = unicodedata.normalize("NFC", value or "").replace("ё", "е").casefold()
    normalized = _PREFIX_RE.sub("", normalized)
    normalized = normalized.replace("упд", "upd").replace("укд", "ukd").replace("мчд", "mcd")
    return " ".join(_TOKEN_RE.findall(normalized))


def is_portal_related(query: str) -> bool:
    return bool(_domain_terms(_tokens(query)))


def rank_historical_matches(query: str, fragments: Iterable[KnowledgeFragment]) -> tuple[HistoricalMatch, ...]:
    matches = tuple(_match(query, fragment) for fragment in fragments if fragment.search_text)
    return tuple(sorted(matches, key=lambda item: (-item.score, item.fragment.fragment_id)))


def _match(query: str, fragment: KnowledgeFragment) -> HistoricalMatch:
    query_tokens = _tokens(query)
    source_tokens = _tokens(fragment.search_text or "")
    query_set = set(query_tokens)
    source_set = set(source_tokens)
    overlap = query_set & source_set
    containment = len(overlap) / max(min(len(query_set), len(source_set)), 1)
    jaccard = len(overlap) / max(len(query_set | source_set), 1)
    sequence = SequenceMatcher(None, normalize_historical_text(query), normalize_historical_text(fragment.search_text or "")).ratio()
    score = round(0.50 * containment + 0.25 * jaccard + 0.25 * sequence, 6)
    domain_overlap = tuple(sorted(_domain_terms(query_tokens) & _domain_terms(source_tokens)))
    action_overlap = tuple(sorted(_action_terms(query_tokens) & _action_terms(source_tokens)))
    contradiction = _contradicts(query, fragment.search_text or "")
    solution = fragment.text.strip()
    meaningful_solution = len(solution) >= 24 and len(_tokens(solution)) >= 3 and _WEAK_SOLUTIONS.fullmatch(solution) is None
    acronym_overlap = bool({"upd", "ukd", "ste", "mcd", "yml", "rdik"} & overlap)
    strong = (
        is_portal_related(query)
        and len(query_set) >= 3
        and len(overlap) >= 3
        and containment >= 0.45
        and score >= 0.53
        and bool(domain_overlap)
        and (bool(action_overlap) or (acronym_overlap and containment >= 0.65))
        and not contradiction
        and meaningful_solution
    )
    return HistoricalMatch(fragment, score, strong, domain_overlap, action_overlap, contradiction)


def _tokens(value: str) -> tuple[str, ...]:
    return tuple(_stem(token) for token in normalize_historical_text(value).split() if token not in _STOPWORDS and len(token) >= 2)


def _stem(token: str) -> str:
    if token.isascii() or token.isdigit() or len(token) < 6:
        return token
    return token[:6]


def _domain_terms(tokens: Iterable[str]) -> set[str]:
    return {token for token in tokens if any(token.startswith(prefix[: min(len(prefix), 6)]) for prefix in _DOMAIN_PREFIXES)}


def _action_terms(tokens: Iterable[str]) -> set[str]:
    return {token for token in tokens if any(token.startswith(prefix[: min(len(prefix), 6)]) for prefix in _ACTION_PREFIXES)}


def _contradicts(left: str, right: str) -> bool:
    left_normalized = normalize_historical_text(left)
    right_normalized = normalize_historical_text(right)
    pairs = (("можно", "нельзя"), ("обязательно", "не обязательно"), ("сохраняется", "не сохраняется"))
    for positive, negative in pairs:
        if (positive in left_normalized and negative in right_normalized) or (negative in left_normalized and positive in right_normalized):
            return True
    return False
