"""pdfplumber baseline extraction with explicit page quality signals."""

from __future__ import annotations

import io
import importlib
from collections import defaultdict
from collections.abc import Mapping
from pathlib import Path
from typing import Any

from .models import ExtractedTable, ExtractionResult, PageExtraction, TextBlock


class ExtractionError(RuntimeError):
    """Raised when a source cannot be opened or parsed as a PDF."""


class PdfPlumberExtractor:
    """Conservative text/table extraction; OCR is intentionally out of scope."""

    extractor_name = "pdfplumber"

    def __init__(self, *, suspicious_text_chars: int = 20) -> None:
        if suspicious_text_chars < 0:
            raise ValueError("suspicious_text_chars must be non-negative")
        self.suspicious_text_chars = suspicious_text_chars

    def extract(self, source: bytes | bytearray | memoryview | str | Path) -> ExtractionResult:
        payload, source_reference = _read_source(source)
        try:
            pdfplumber = importlib.import_module("pdfplumber")
        except ImportError as exc:  # pragma: no cover - dependency is project runtime
            raise ExtractionError("pdfplumber is required for baseline PDF extraction") from exc

        pages: list[PageExtraction] = []
        global_warnings: list[str] = []
        try:
            with pdfplumber.open(io.BytesIO(payload)) as pdf:
                for page_number, page in enumerate(pdf.pages, start=1):
                    pages.append(self._extract_page(page, page_number))
        except Exception as exc:
            raise ExtractionError(f"Unable to extract PDF source {source_reference}") from exc
        return ExtractionResult(
            source_reference=source_reference,
            page_count=len(pages),
            pages=tuple(pages),
            warnings=tuple(global_warnings),
            extractor=self.extractor_name,
            extractor_version=getattr(pdfplumber, "__version__", None),
        )

    def _extract_page(self, page: Any, page_number: int) -> PageExtraction:
        warnings: list[str] = []
        quality_flags: list[str] = []
        try:
            raw_text = page.extract_text() or ""
        except Exception as exc:
            raw_text = ""
            warnings.append(f"text extraction failed: {type(exc).__name__}")
            quality_flags.append("TEXT_EXTRACTION_FAILED")

        blocks = self._extract_blocks(page, raw_text, warnings)
        tables = self._extract_tables(page, warnings)
        normalized_text = raw_text.replace("\r\n", "\n").replace("\r", "\n").strip()
        if not normalized_text and not tables:
            quality_flags.append("EMPTY_PAGE")
        elif len(normalized_text) < self.suspicious_text_chars and not tables:
            quality_flags.append("LOW_TEXT_DENSITY")
        if any(table.warnings for table in tables):
            quality_flags.append("TABLE_STRUCTURE_WARNING")
        if warnings:
            quality_flags.append("EXTRACTION_WARNING")
        return PageExtraction(
            page_number=page_number,
            text=normalized_text,
            text_blocks=tuple(blocks),
            tables=tuple(tables),
            quality_flags=tuple(dict.fromkeys(quality_flags)),
            warnings=tuple(warnings),
        )

    @staticmethod
    def _extract_blocks(page: Any, raw_text: str, warnings: list[str]) -> list[TextBlock]:
        try:
            words = page.extract_words(use_text_flow=True, keep_blank_chars=False) or []
        except Exception as exc:
            warnings.append(f"word extraction failed: {type(exc).__name__}")
            words = []
        if not words:
            return [TextBlock(text=raw_text.strip())] if raw_text.strip() else []

        lines: dict[float, list[Mapping[str, object]]] = defaultdict(list)
        for word in words:
            if not isinstance(word, Mapping) or not str(word.get("text", "")).strip():
                continue
            try:
                key = round(float(word.get("top", 0.0)), 1)
            except (TypeError, ValueError):
                key = 0.0
            lines[key].append(word)
        blocks: list[TextBlock] = []
        for words_on_line in lines.values():
            ordered = sorted(words_on_line, key=lambda word: _number(word.get("x0")))
            text = " ".join(str(word.get("text", "")).strip() for word in ordered).strip()
            if not text:
                continue
            coordinates = [_bbox(word) for word in ordered]
            valid = [bbox for bbox in coordinates if bbox is not None]
            bbox = None
            if valid:
                bbox = (
                    min(item[0] for item in valid),
                    min(item[1] for item in valid),
                    max(item[2] for item in valid),
                    max(item[3] for item in valid),
                )
            blocks.append(TextBlock(text=text, bbox=bbox))
        return blocks

    @staticmethod
    def _extract_tables(page: Any, warnings: list[str]) -> list[ExtractedTable]:
        try:
            raw_tables = page.extract_tables() or []
        except Exception as exc:
            warnings.append(f"table extraction failed: {type(exc).__name__}")
            return []
        found_boxes: list[tuple[float, float, float, float] | None] = []
        try:
            found_boxes = [_bbox_from_table(table) for table in (page.find_tables() or [])]
        except Exception:
            # Table text remains useful even if geometry discovery is unavailable.
            found_boxes = []
        tables: list[ExtractedTable] = []
        for index, raw_table in enumerate(raw_tables):
            rows = [tuple(_clean_cell(cell) for cell in row) for row in raw_table if row]
            if not rows:
                continue
            width = max(len(row) for row in rows)
            padded = [row + (None,) * (width - len(row)) for row in rows]
            headers = tuple(cell or "" for cell in padded[0])
            row_values = tuple(padded[1:])
            table_warnings: list[str] = []
            if any(cell is None for row in padded for cell in row):
                table_warnings.append("one or more table cells were empty")
            tables.append(
                ExtractedTable(
                    headers=headers,
                    rows=row_values,
                    bbox=found_boxes[index] if index < len(found_boxes) else None,
                    warnings=tuple(table_warnings),
                )
            )
        return tables


def _read_source(source: bytes | bytearray | memoryview | str | Path) -> tuple[bytes, str]:
    if isinstance(source, (bytes, bytearray, memoryview)):
        return bytes(source), "<memory>"
    path = Path(source).expanduser()
    if not path.is_file():
        raise FileNotFoundError(path)
    return path.read_bytes(), path.as_posix()


def _number(value: object) -> float:
    try:
        return float(value)  # type: ignore[arg-type]
    except (TypeError, ValueError):
        return 0.0


def _bbox(word: Mapping[str, object]) -> tuple[float, float, float, float] | None:
    values = tuple(_number(word.get(key)) for key in ("x0", "top", "x1", "bottom"))
    if values == (0.0, 0.0, 0.0, 0.0) and not any(key in word for key in ("x0", "top", "x1", "bottom")):
        return None
    return values


def _bbox_from_table(table: object) -> tuple[float, float, float, float] | None:
    bbox = getattr(table, "bbox", None)
    if not isinstance(bbox, (tuple, list)) or len(bbox) != 4:
        return None
    return tuple(float(value) for value in bbox)  # type: ignore[return-value]


def _clean_cell(value: object) -> str | None:
    if value is None:
        return None
    cleaned = " ".join(str(value).replace("\r", "\n").split())
    return cleaned or None
