"""Deterministic output boundary for private model reasoning."""

from __future__ import annotations

import re

from .errors import GeneratorClientError


_THINK_RE = re.compile(r"<think>.*?</think>", re.IGNORECASE | re.DOTALL)


def strip_reasoning(text: str) -> str:
    """Remove private ``<think>`` spans and fail closed on malformed output."""

    if not isinstance(text, str):
        raise GeneratorClientError("local generator returned non-text content")
    lowered = text.casefold()
    opening = lowered.find("<think>")
    closing = lowered.find("</think>")
    if opening >= 0 and closing < 0:
        raise GeneratorClientError("local generator returned unterminated private reasoning")
    if opening >= 0 and closing < opening:
        raise GeneratorClientError("local generator returned malformed reasoning markers")
    cleaned = _THINK_RE.sub("", text)
    if "<think>" in cleaned.casefold() or "</think>" in cleaned.casefold():
        raise GeneratorClientError("local generator reasoning boundary failed")
    return cleaned.strip()
