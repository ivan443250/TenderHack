"""Deterministic query normalization and exact-entity extraction."""

from __future__ import annotations

import re
import unicodedata
from dataclasses import dataclass

from tenderhack_knowledge.contracts.v0 import Entity, EntityProvenance, UnderstandResponse


# Keep source ASCII-only: Python/regex interpret these escapes as Unicode at
# runtime, independent of the Windows console code page.
_CYRILLIC = r"\u0410-\u042f\u0430-\u044f\u0401\u0451"
_TOKEN_RE = re.compile(
    rf"(?<![\w])(?:[A-Za-z{_CYRILLIC}]+[A-Za-z{_CYRILLIC}0-9]*(?:[-_/][A-Za-z{_CYRILLIC}0-9]+)+|[A-Za-z{_CYRILLIC}]{{2,8}}|\d+)(?![\w])"
)
_DURATION_TERMS = (
    "\u0441\u0435\u043a\u0443\u043d\u0434\u0430", "\u0441\u0435\u043a\u0443\u043d\u0434\u044b", "\u0441\u0435\u043a\u0443\u043d\u0434",
    "\u0441", "\u043c\u0438\u043d\u0443\u0442\u0430", "\u043c\u0438\u043d\u0443\u0442\u044b", "\u043c\u0438\u043d\u0443\u0442", "\u043c\u0438\u043d",
    "\u0447\u0430\u0441\u0430", "\u0447\u0430\u0441\u043e\u0432", "\u0447\u0430\u0441", "\u0447", "\u0434\u043d\u044f", "\u0434\u043d\u0435\u0439", "\u0434\u0435\u043d\u044c",
    "\u043c\u0435\u0441\u044f\u0446\u0430", "\u043c\u0435\u0441\u044f\u0446\u0435\u0432", "\u043c\u0435\u0441\u044f\u0446", "\u043c\u0435\u0441",
)
_DURATION_RE = re.compile(r"(?<!\w)\d+(?:[.,]\d+)?\s*(?:" + "|".join(map(re.escape, _DURATION_TERMS)) + r")\b", re.IGNORECASE)
_STATUS_TERMS = (
    "\u0432 \u043e\u0447\u0435\u0440\u0435\u0434\u0438", "\u043e\u0431\u0440\u0430\u0431\u043e\u0442\u043a\u0430", "\u043e\u0431\u0440\u0430\u0431\u043e\u0442\u043a\u0435", "\u043e\u0431\u0440\u0430\u0431\u043e\u0442\u043a\u0438",
    "\u043e\u0448\u0438\u0431\u043a\u0430 \u043f\u0440\u0438 \u043e\u0431\u0440\u0430\u0431\u043e\u0442\u043a\u0435", "\u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043d\u0430 \u0443\u0441\u043f\u0435\u0448\u043d\u043e", "\u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043d \u0443\u0441\u043f\u0435\u0448\u043d\u043e",
    "\u043f\u043e\u0434\u043f\u0438\u0441\u0430\u043d\u043e \u043f\u043e\u0441\u0442\u0430\u0432\u0449\u0438\u043a\u043e\u043c", "\u043f\u043e\u0434\u043f\u0438\u0441\u0430\u043d\u0430 \u043f\u043e\u0441\u0442\u0430\u0432\u0449\u0438\u043a\u043e\u043c",
    "\u043d\u0430 \u0440\u0430\u0441\u0441\u043c\u043e\u0442\u0440\u0435\u043d\u0438\u0438 \u0437\u0430\u043a\u0430\u0437\u0447\u0438\u043a\u0430", "\u043e\u0442\u043f\u0440\u0430\u0432\u043b\u0435\u043d\u043e \u043e\u043f\u0435\u0440\u0430\u0442\u043e\u0440\u0443 \u044d\u0434\u043e",
)
_STATUS_RE = re.compile(r"\b(?:" + "|".join(map(re.escape, _STATUS_TERMS)) + r")\b", re.IGNORECASE)
_ABBREVIATIONS = {
    "\u0443\u043f\u0434", "\u0443\u043a\u0434", "\u043c\u0447\u0434", "yml", "\u0441\u0442\u0435", "\u044d\u0434\u043e", "\u044d\u043f", "\u0440\u0434\u0438\u043a", "rdik",
}
_TECHNICAL_WORDS = {"pdf", "xml", "xlsx", "xls", "csp", "diadoc", "\u043a\u0430\u043b\u0443\u0433\u0430", "\u0430\u0441\u0442\u0440\u0430\u043b"}


@dataclass(frozen=True)
class ExtractedEntity:
    """Internal entity retaining provenance beyond the frozen wire DTO."""

    type: str
    original: str
    normalized: str
    provenance: str


@dataclass(frozen=True)
class Understanding:
    original_query: str
    normalized_query: str
    entities: tuple[ExtractedEntity, ...]
    exact_codes: tuple[str, ...]
    detected_language: str
    typo_corrected: bool = False

    def to_contract(self) -> UnderstandResponse:
        return UnderstandResponse(
            normalized_text=self.normalized_query,
            entities=[
                Entity(
                    type=entity.type,
                    value=entity.original,
                    provenance=(
                        EntityProvenance.USER_EXPLICIT
                        if entity.provenance == "ORIGINAL"
                        else EntityProvenance.INFERRED
                    ),
                )
                for entity in self.entities
            ],
            exact_codes=list(self.exact_codes),
            language_flags={
                "detected_language": self.detected_language,
                "typo_corrected": self.typo_corrected,
            },
        )


def normalize_query(query: str) -> str:
    """Return a conservative search representation without changing source text."""

    if not isinstance(query, str):
        raise TypeError("query must be a string")
    normalized = unicodedata.normalize("NFC", query).replace("\u00a0", " ")
    normalized = normalized.replace("\u0451", "\u0435").replace("\u0401", "\u0415")
    normalized = re.sub(r"\s+", " ", normalized).strip()
    return normalized.casefold()


def understand_query(query: str) -> Understanding:
    normalized = normalize_query(query)
    entities: list[ExtractedEntity] = []
    exact_codes: list[str] = []
    seen: set[tuple[str, str]] = set()

    def add(entity_type: str, original: str, provenance: str = "ORIGINAL") -> None:
        key = (entity_type, normalize_query(original))
        if key in seen:
            return
        seen.add(key)
        entities.append(ExtractedEntity(entity_type, original, normalize_query(original), provenance))

    for match in _TOKEN_RE.finditer(query):
        token = match.group(0)
        token_normalized = normalize_query(token)
        has_digit = any(character.isdigit() for character in token)
        has_separator = any(character in token for character in "-/_")
        if token_normalized in _ABBREVIATIONS:
            add("document_abbreviation", token)
            exact_codes.append(token)
        elif token_normalized in _TECHNICAL_WORDS:
            add("technical_token", token)
            exact_codes.append(token)
        elif has_digit and (has_separator or len(token) >= 2):
            add("exact_code" if has_separator else "numeric_identifier", token)
            exact_codes.append(token)
        elif token.isdigit() and len(token) >= 2:
            add("numeric_identifier", token)

    for match in _DURATION_RE.finditer(query):
        add("duration", match.group(0))
    for match in _STATUS_RE.finditer(query):
        add("status", match.group(0))

    exact_codes = list(dict.fromkeys(exact_codes))
    detected_language = "ru" if re.search(rf"[{_CYRILLIC}]", query, re.IGNORECASE) else "unknown"
    return Understanding(
        original_query=query,
        normalized_query=normalized,
        entities=tuple(entities),
        exact_codes=tuple(exact_codes),
        detected_language=detected_language,
    )


def understand_response(query: str) -> UnderstandResponse:
    return understand_query(query).to_contract()
