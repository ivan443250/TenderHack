"""Deterministic import of the organizer-provided STP XLSX export.

The source workbook is opened read-only and never modified. Only sanitized
ticket descriptions and verified solution fields enter the historical corpus.
"""

from __future__ import annotations

import hashlib
import re
import unicodedata
import xml.etree.ElementTree as ET
import zipfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

from tenderhack_knowledge.ingestion.ids import (
    make_document_id,
    make_document_version_id,
    make_fragment_id,
    make_ingestion_run_id,
    text_hash,
)
from tenderhack_knowledge.ingestion.models import (
    Corpus,
    Document,
    DocumentVersion,
    IngestionRun,
    KnowledgeFragment,
    SourceAnchor,
)


HISTORICAL_KIND = "HISTORICAL_SUPPORT_SOLUTION"
HISTORICAL_SOURCE_TITLE = "Проверенное решение службы поддержки"
_XML_NS = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
_CELL_COLUMN_RE = re.compile(r"[A-Z]+")
_WHITESPACE_RE = re.compile(r"\s+")

_PII_PATTERNS: tuple[tuple[str, re.Pattern[str], str], ...] = (
    ("email", re.compile(r"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b"), "[EMAIL]"),
    (
        "phone",
        re.compile(r"(?<!\d)(?:\+7|8)[\s()\-]*\d{3}[\s()\-]*\d{3}[\s\-]*\d{2}[\s\-]*\d{2}(?!\d)"),
        "[PHONE]",
    ),
    ("uuid", re.compile(r"(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b"), "[IDENTIFIER]"),
    (
        "labelled_identifier",
        re.compile(r"(?i)\b(?:ИНН|КПП|ОГРН|СНИЛС|логин|идентификатор(?:\s+пользователя)?|user\s*id)\s*[:№#-]?\s*[A-ZА-ЯЁ0-9._-]{4,}"),
        "[IDENTIFIER]",
    ),
    ("long_identifier", re.compile(r"(?<!\d)\d{8,}(?!\d)"), "[IDENTIFIER]"),
    (
        "labelled_name",
        re.compile(r"(?i)\b(?:ФИО|контактное\s+лицо|заявитель|пользователь)\s*[:№#-]?\s*[А-ЯЁ][а-яё-]+(?:\s+[А-ЯЁ][а-яё-]+){1,2}"),
        "[PERSON]",
    ),
)


@dataclass(frozen=True)
class StpRecord:
    source_row: int
    question: str
    solution: str
    topic: str | None
    subtopic: str | None
    question_hash: str
    redactions: int


@dataclass(frozen=True)
class StpDataset:
    path: Path
    sha256: str
    rows_total: int
    rows_with_nonempty_solution: int
    question_column: str
    solution_column: str
    topic_column: str | None
    subtopic_column: str | None
    records: tuple[StpRecord, ...]
    pii_redactions: int


@dataclass(frozen=True)
class HistoricalImportSummary:
    dataset_sha256: str
    rows_total: int
    rows_indexed: int
    pii_redactions: int
    snapshot_id: str
    document_version_id: str
    idempotent: bool


def sanitize_support_text(value: str) -> tuple[str, int]:
    """Remove common direct identifiers without retaining the matched value."""

    sanitized = unicodedata.normalize("NFC", value or "").replace("\u00a0", " ")
    redactions = 0
    for _name, pattern, replacement in _PII_PATTERNS:
        sanitized, count = pattern.subn(replacement, sanitized)
        redactions += count
    return _WHITESPACE_RE.sub(" ", sanitized).strip(), redactions


def read_stp_dataset(path: Path) -> StpDataset:
    path = path.resolve(strict=True)
    digest = _sha256_file(path)
    rows = tuple(_xlsx_rows(path))
    if not rows:
        raise ValueError("STP workbook has no rows")
    headers = {value.strip(): column for column, value in rows[0].items() if value.strip()}
    question_column = headers.get("Описание")
    solution_column = headers.get("Решение")
    if question_column is None or solution_column is None:
        raise ValueError("STP workbook must contain Описание and Решение columns")
    topic_column = headers.get("Тема")
    subtopic_column = headers.get("Подтема")

    records: list[StpRecord] = []
    redactions_total = 0
    rows_with_solution = 0
    for source_row, row in enumerate(rows[1:], start=2):
        raw_question = row.get(question_column, "").strip()
        raw_solution = row.get(solution_column, "").strip()
        if raw_solution:
            rows_with_solution += 1
        if not raw_question or not raw_solution:
            continue
        question, question_redactions = sanitize_support_text(raw_question)
        solution, solution_redactions = sanitize_support_text(raw_solution)
        if not question or not solution:
            continue
        topic, topic_redactions = sanitize_support_text(row.get(topic_column, "")) if topic_column else ("", 0)
        subtopic, subtopic_redactions = sanitize_support_text(row.get(subtopic_column, "")) if subtopic_column else ("", 0)
        redactions = question_redactions + solution_redactions + topic_redactions + subtopic_redactions
        redactions_total += redactions
        records.append(
            StpRecord(
                source_row=source_row,
                question=question,
                solution=solution,
                topic=topic or None,
                subtopic=subtopic or None,
                question_hash=hashlib.sha256(question.casefold().encode("utf-8")).hexdigest(),
                redactions=redactions,
            )
        )
    return StpDataset(
        path=path,
        sha256=digest,
        rows_total=len(rows) - 1,
        rows_with_nonempty_solution=rows_with_solution,
        question_column="Описание",
        solution_column="Решение",
        topic_column="Тема" if topic_column else None,
        subtopic_column="Подтема" if subtopic_column else None,
        records=tuple(records),
        pii_redactions=redactions_total,
    )


async def import_stp_dataset(path: Path, repository: object) -> HistoricalImportSummary:
    dataset = read_stp_dataset(path)
    document = Document(
        document_id=make_document_id(dataset.path.name, dataset.path.name),
        original_filename=dataset.path.name,
        corpus=Corpus.HISTORICAL,
    )
    version_id = make_document_version_id(document.document_id, dataset.sha256, dataset.sha256, None)
    now = datetime.now(timezone.utc)
    version = DocumentVersion(
        document_version_id=version_id,
        document_id=document.document_id,
        content_sha256=dataset.sha256,
        declared_version=dataset.sha256,
        page_count=0,
        source_reference=dataset.path.name,
        ingested_at=now,
        review_status="VERIFIED",
    )
    fragments = tuple(_record_fragment(record, version_id) for record in dataset.records)
    run = IngestionRun(
        run_id=make_ingestion_run_id(Corpus.HISTORICAL.value, version_id, "stp-xlsx-v1"),
        corpus=Corpus.HISTORICAL,
        status="COMPLETED",
        source_count=len(fragments),
        document_version_ids=(version_id,),
        started_at=now,
        completed_at=now,
    )
    await repository.register_document(document)
    _stored_version, _stored_fragments, _stored_run, idempotent = await repository.store_version(version, fragments, run)
    snapshot = await repository.publish_snapshot((version_id,), Corpus.HISTORICAL)
    return HistoricalImportSummary(
        dataset_sha256=dataset.sha256,
        rows_total=dataset.rows_total,
        rows_indexed=len(fragments),
        pii_redactions=dataset.pii_redactions,
        snapshot_id=snapshot.snapshot_id,
        document_version_id=version_id,
        idempotent=idempotent,
    )


def _record_fragment(record: StpRecord, version_id: str) -> KnowledgeFragment:
    solution_hash = text_hash(record.solution)
    section = record.topic
    anchor = f"stp-row:{record.source_row}"
    fragment_id = make_fragment_id(version_id, 1, 1, section, anchor, solution_hash)
    headings = tuple(value for value in (record.topic, record.subtopic) if value)
    return KnowledgeFragment(
        fragment_id=fragment_id,
        document_version_id=version_id,
        page_start=1,
        page_end=1,
        section=section,
        kind=HISTORICAL_KIND,
        heading_path=headings,
        search_text=record.question,
        text=record.solution,
        source_anchor=SourceAnchor(page=1, section=section, text_hash=solution_hash),
        process=record.subtopic,
        review_status="VERIFIED",
        text_hash=solution_hash,
    )


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _xlsx_rows(path: Path) -> Iterable[dict[str, str]]:
    with zipfile.ZipFile(path) as workbook:
        shared = _shared_strings(workbook)
        with workbook.open("xl/worksheets/sheet1.xml") as sheet:
            for _event, element in ET.iterparse(sheet, events=("end",)):
                if element.tag != _XML_NS + "row":
                    continue
                row: dict[str, str] = {}
                for cell in element.findall(_XML_NS + "c"):
                    reference = cell.attrib.get("r", "")
                    match = _CELL_COLUMN_RE.match(reference)
                    if match is None:
                        continue
                    cell_type = cell.attrib.get("t")
                    value_node = cell.find(_XML_NS + "v")
                    if cell_type == "s" and value_node is not None:
                        value = shared[int(value_node.text or "0")]
                    elif cell_type == "inlineStr":
                        value = "".join(node.text or "" for node in cell.iter(_XML_NS + "t"))
                    else:
                        value = value_node.text if value_node is not None and value_node.text else ""
                    row[match.group(0)] = value
                yield row
                element.clear()


def _shared_strings(workbook: zipfile.ZipFile) -> tuple[str, ...]:
    root = ET.fromstring(workbook.read("xl/sharedStrings.xml"))
    return tuple("".join(node.text or "" for node in item.iter(_XML_NS + "t")) for item in root.findall(_XML_NS + "si"))
