"""Knowledge-owned ingestion and provenance models.

These are deliberately persistence-neutral.  They model source facts and
evidence provenance, not retrieval scores or support-core decisions.
"""

from __future__ import annotations

from datetime import date, datetime
from enum import Enum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class Corpus(str, Enum):
    NORMATIVE = "NORMATIVE"
    HISTORICAL = "HISTORICAL"


class SourceAnchor(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    page: int = Field(ge=1)
    bbox: tuple[float, float, float, float] | None = None
    section: str | None = None
    text_excerpt: str | None = None
    text_hash: str


class Document(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    document_id: str
    original_filename: str
    corpus: Corpus


class DocumentVersion(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    document_version_id: str
    document_id: str
    content_sha256: str
    declared_version: str | None = None
    declared_date: date | None = None
    page_count: int = Field(ge=0)
    source_reference: str
    ingested_at: datetime
    review_status: str = "PENDING_REVIEW"


class KnowledgeFragment(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    fragment_id: str
    document_version_id: str
    snapshot_id: str | None = None
    page_start: int = Field(ge=1)
    page_end: int = Field(ge=1)
    section: str | None = None
    kind: str
    heading_path: tuple[str, ...] = ()
    # Normative fragments search and cite the same source text. Historical
    # support records search the sanitized ticket description but cite only
    # the separately sanitized, organizer-verified solution.
    search_text: str | None = None
    text: str
    source_anchor: SourceAnchor
    actor_roles: tuple[str, ...] | None = None
    process: str | None = None
    provider: str | None = None
    document_status: str | None = None
    error_codes: tuple[str, ...] | None = None
    review_status: str = "PENDING_REVIEW"
    text_hash: str


class KnowledgeSnapshot(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    snapshot_id: str
    corpus: Corpus
    document_version_ids: tuple[str, ...]
    manifest_hash: str
    created_at: datetime
    review_status: str = "PENDING_REVIEW"


class IngestionRun(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    run_id: str
    corpus: Corpus
    status: str
    source_count: int = Field(ge=0)
    document_version_ids: tuple[str, ...] = ()
    started_at: datetime
    completed_at: datetime | None = None
    warnings: tuple[str, ...] = ()
    error: str | None = None


class TextBlock(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    text: str
    bbox: tuple[float, float, float, float] | None = None


class ExtractedTable(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    headers: tuple[str, ...]
    rows: tuple[tuple[str | None, ...], ...]
    bbox: tuple[float, float, float, float] | None = None
    warnings: tuple[str, ...] = ()


class PageExtraction(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    page_number: int = Field(ge=1)
    text: str
    text_blocks: tuple[TextBlock, ...] = ()
    tables: tuple[ExtractedTable, ...] = ()
    quality_flags: tuple[str, ...] = ()
    warnings: tuple[str, ...] = ()


class ExtractionResult(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    source_reference: str
    page_count: int = Field(ge=0)
    pages: tuple[PageExtraction, ...]
    warnings: tuple[str, ...] = ()
    extractor: str = "pdfplumber"
    extractor_version: str | None = None


class SourceResolution(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    fragment_id: str
    document_id: str
    document_version_id: str
    original_filename: str
    source_reference: str
    snapshot_id: str | None
    page_start: int
    page_end: int
    section: str | None
    kind: str
    heading_path: tuple[str, ...]
    text: str
    source_anchor: SourceAnchor
    review_status: str
    declared_version: str | None = None
    declared_date: date | None = None
    page_count: int | None = Field(default=None, ge=0)


class IngestionResult(BaseModel):
    model_config = ConfigDict(extra="forbid", arbitrary_types_allowed=True)

    document: Document
    document_version: DocumentVersion
    fragments: tuple[KnowledgeFragment, ...]
    ingestion_run: IngestionRun
    snapshot: KnowledgeSnapshot
    idempotent: bool = False


class PreparedIngestion(BaseModel):
    """Validated source facts ready for repository persistence.

    Keeping preparation separate from publication lets a multi-document
    bootstrap validate the complete corpus before writing anything and then
    publish one deterministic snapshot for the whole manifest.
    """

    model_config = ConfigDict(extra="forbid", arbitrary_types_allowed=True)

    document: Document
    document_version: DocumentVersion
    fragments: tuple[KnowledgeFragment, ...]
    ingestion_run: IngestionRun


def model_payload(model: BaseModel) -> dict[str, Any]:
    """Return JSON-compatible persistence payload for a knowledge model."""

    return model.model_dump(mode="json")
