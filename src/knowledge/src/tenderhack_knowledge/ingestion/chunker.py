"""Conservative structure recovery and semantic fragment construction."""

from __future__ import annotations

import re
from collections import defaultdict
from dataclasses import dataclass

from .ids import make_fragment_id, text_hash
from .models import DocumentVersion, ExtractionResult, KnowledgeFragment, PageExtraction


_HEADING_RE = re.compile(r"^\s*(?:\d+(?:\.\d+)*[.)]?|[A-ZА-ЯЁ][A-ZА-ЯЁ\s\-]{3,})\s+\S")
_STEP_RE = re.compile(r"^\s*(?:\d+[.)]|[-–—•])\s+\S")
_CONDITION_RE = re.compile(r"^\s*(?:если|при условии|при отсутствии|в случае|when|if)\b", re.IGNORECASE)
_WARNING_RE = re.compile(r"\b(?:внимание|важно|предупреждение|запрещено|warning|important)\b", re.IGNORECASE)
_STATUS_RE = re.compile(r"\b(?:статус|status|состояние|закрыт|отклонен|принят|active|closed)\b", re.IGNORECASE)
_DEFINITION_RE = re.compile(r"\s(?:—|-|:)\s")


@dataclass(frozen=True)
class _TextUnit:
    kind: str
    text: str
    page: int
    bbox: tuple[float, float, float, float] | None
    heading_path: tuple[str, ...]


class SemanticFragmentBuilder:
    """Build searchable fragments without blind fixed-character splitting."""

    def __init__(self, *, max_embedding_tokens: int = 450) -> None:
        if max_embedding_tokens < 1:
            raise ValueError("max_embedding_tokens must be positive")
        self.max_embedding_tokens = max_embedding_tokens

    def build(self, version: DocumentVersion, extraction: ExtractionResult) -> tuple[KnowledgeFragment, ...]:
        fragments: list[KnowledgeFragment] = []
        for page in extraction.pages:
            units = self._page_units(page)
            for index, (kind, text, bbox, heading_path) in enumerate(self._pack_units(units)):
                canonical_text = text.strip()
                if not canonical_text:
                    continue
                digest = text_hash(canonical_text)
                section = " / ".join(heading_path) if heading_path else None
                anchor_key = f"page:{page.page_number};unit:{index};bbox:{bbox}"
                review_status = "NEEDS_REVIEW" if page.quality_flags else version.review_status
                fragments.append(
                    KnowledgeFragment(
                        fragment_id=make_fragment_id(
                            version.document_version_id,
                            page.page_number,
                            page.page_number,
                            section,
                            anchor_key,
                            digest,
                        ),
                        document_version_id=version.document_version_id,
                        snapshot_id=None,
                        page_start=page.page_number,
                        page_end=page.page_number,
                        section=section,
                        kind=kind,
                        heading_path=heading_path,
                        text=canonical_text,
                        source_anchor={
                            "page": page.page_number,
                            "bbox": bbox,
                            "section": section,
                            "text_excerpt": canonical_text[:240],
                            "text_hash": digest,
                        },
                        actor_roles=None,
                        process=None,
                        provider=None,
                        document_status=None,
                        error_codes=None,
                        review_status=review_status,
                        text_hash=digest,
                    )
                )
        return tuple(fragments)

    def _page_units(self, page: PageExtraction) -> list[_TextUnit]:
        units: list[_TextUnit] = []
        heading_path: list[str] = []
        paragraph_lines: list[str] = []
        paragraph_boxes: list[tuple[float, float, float, float]] = []

        blocks_by_text: dict[str, list[object]] = defaultdict(list)
        for block in page.text_blocks:
            block_text = block.text.strip()
            if block_text:
                blocks_by_text[block_text].append(block)
        block_offsets: dict[str, int] = defaultdict(int)

        def take_block(text: str) -> object | None:
            candidates = blocks_by_text.get(text)
            offset = block_offsets[text]
            if not candidates or offset >= len(candidates):
                return None
            block_offsets[text] = offset + 1
            return candidates[offset]

        def flush_paragraph() -> None:
            if not paragraph_lines:
                return
            text = "\n".join(paragraph_lines).strip()
            units.append(
                _TextUnit(
                    kind=_classify(text),
                    text=_with_heading_context(text, heading_path),
                    page=page.page_number,
                    bbox=_union_boxes(paragraph_boxes),
                    heading_path=tuple(heading_path),
                )
            )
            paragraph_lines.clear()
            paragraph_boxes.clear()

        for line in page.text.splitlines():
            stripped = line.strip()
            if not stripped:
                flush_paragraph()
                continue
            block = take_block(stripped)
            if _is_heading(stripped):
                flush_paragraph()
                heading_path = _update_heading_path(heading_path, stripped)
                continue
            if _STEP_RE.match(stripped) or _WARNING_RE.search(stripped):
                flush_paragraph()
                units.append(
                    _TextUnit(
                        kind=_classify(stripped),
                        text=_with_heading_context(stripped, heading_path),
                        page=page.page_number,
                        bbox=getattr(block, "bbox", None),
                        heading_path=tuple(heading_path),
                    )
                )
                continue
            paragraph_lines.append(stripped)
            if block and block.bbox:
                paragraph_boxes.append(block.bbox)
        flush_paragraph()
        if not units and heading_path:
            heading_text = "Section: " + " / ".join(heading_path)
            units.append(
                _TextUnit(
                    kind="definition",
                    text=heading_text,
                    page=page.page_number,
                    bbox=None,
                    heading_path=tuple(heading_path),
                )
            )

        heading_markers: list[tuple[float, tuple[str, ...]]] = []
        marker_path: list[str] = []
        for block in page.text_blocks:
            block_text = block.text.strip()
            if not _is_heading(block_text):
                continue
            marker_path = _update_heading_path(marker_path, block_text)
            if block.bbox is not None:
                heading_markers.append((block.bbox[1], tuple(marker_path)))

        for table in page.tables:
            headers = [header.strip() for header in table.headers]
            header_prefix = " | ".join(headers)
            table_heading_path = tuple(heading_path)
            if table.bbox is not None and heading_markers:
                prior = [path for top, path in heading_markers if top <= table.bbox[1]]
                if prior:
                    table_heading_path = prior[-1]
            for row in table.rows:
                cells = [(cell or "").strip() for cell in row]
                pairs = [f"{headers[index] or f'Column {index + 1}'}: {cells[index]}" for index in range(len(cells))]
                row_text = f"{header_prefix} | " + " | ".join(pairs)
                units.append(
                    _TextUnit(
                        kind="table_row",
                        text=_with_heading_context(row_text, list(table_heading_path)),
                        page=page.page_number,
                        bbox=table.bbox,
                        heading_path=table_heading_path,
                    )
                )
        return units

    def _pack_units(self, units: list[_TextUnit]) -> list[tuple[str, str, tuple[float, float, float, float] | None, tuple[str, ...]]]:
        packed: list[tuple[str, str, tuple[float, float, float, float] | None, tuple[str, ...]]] = []
        current: list[_TextUnit] = []
        current_tokens = 0

        def flush() -> None:
            nonlocal current, current_tokens
            if not current:
                return
            text = "\n".join(unit.text for unit in current)
            kinds = {unit.kind for unit in current}
            if len(kinds) == 1:
                kind = current[0].kind
            elif kinds <= {"procedure_step", "procedure"}:
                kind = "procedure"
            else:
                kind = "paragraph"
            packed.append((kind, text, _union_boxes([unit.bbox for unit in current]), current[0].heading_path))
            current = []
            current_tokens = 0

        for unit in units:
            unit_tokens = _approx_tokens(unit.text)
            if unit.kind in {"table_row", "condition", "warning", "status_rule", "procedure_step"}:
                flush()
                packed.append((unit.kind, unit.text, unit.bbox, unit.heading_path))
                continue
            # A unit is semantic/atomic: a long condition, step, or table row
            # is kept whole rather than cut at an arbitrary character offset.
            if current and current_tokens + unit_tokens > self.max_embedding_tokens:
                flush()
            current.append(unit)
            current_tokens += unit_tokens
        flush()
        return packed


def _is_heading(text: str) -> bool:
    if len(text) > 140 or text.endswith((".", ";", ":")):
        return False
    if _HEADING_RE.match(text):
        return True
    return text.isupper() and len(text.split()) <= 12


def _update_heading_path(path: list[str], heading: str) -> list[str]:
    match = re.match(r"^\s*(\d+(?:\.\d+)*)", heading)
    if not match:
        return [heading]
    depth = match.group(1).count(".") + 1
    return [*path[: depth - 1], heading]


def _classify(text: str) -> str:
    if _CONDITION_RE.match(text):
        return "condition"
    if _WARNING_RE.search(text):
        return "warning"
    if _STATUS_RE.search(text) and (":" in text or "—" in text or "-" in text):
        return "status_rule"
    if _STEP_RE.match(text):
        return "procedure_step"
    if "```" in text or re.search(r"\b(?:SELECT|INSERT|curl|python)\b", text):
        return "code_example"
    if _DEFINITION_RE.search(text):
        return "definition"
    return "paragraph"


def _with_heading_context(text: str, heading_path: list[str]) -> str:
    if not heading_path:
        return text
    return f"Section: {' / '.join(heading_path)}\n{text}"


def _approx_tokens(text: str) -> int:
    # A deliberately documented estimate; tokenizer-specific counts belong to
    # the later embedding benchmark, not this provenance foundation.
    return max(1, (len(text) + 3) // 4)


def _union_boxes(boxes: list[tuple[float, float, float, float] | None]) -> tuple[float, float, float, float] | None:
    valid = [box for box in boxes if box is not None]
    if not valid:
        return None
    return (
        min(box[0] for box in valid),
        min(box[1] for box in valid),
        max(box[2] for box in valid),
        max(box[3] for box in valid),
    )
