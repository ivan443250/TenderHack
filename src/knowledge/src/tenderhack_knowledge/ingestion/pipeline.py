"""Document inventory, extraction, fragmenting and snapshot publication."""

from __future__ import annotations

from collections.abc import Callable
from datetime import date, datetime, timezone
from pathlib import Path

from .chunker import SemanticFragmentBuilder
from .extractor import PdfPlumberExtractor, _read_source
from .ids import (
    make_document_id,
    make_document_version_id,
    make_ingestion_run_id,
    normalize_source_name,
    sha256_bytes,
)
from .models import Corpus, Document, DocumentVersion, IngestionResult, IngestionRun
from .repository import InMemoryKnowledgeRepository


class IngestionPipeline:
    """Idempotent, single-document foundation pipeline.

    The pipeline intentionally publishes a snapshot containing the ingested
    version only. Multi-document manifest composition remains an explicit
    repository operation so callers cannot accidentally mix corpora.
    """

    def __init__(
        self,
        repository: InMemoryKnowledgeRepository | None = None,
        *,
        extractor: PdfPlumberExtractor | None = None,
        fragment_builder: SemanticFragmentBuilder | None = None,
        clock: Callable[[], datetime] | None = None,
    ) -> None:
        self.repository = repository or InMemoryKnowledgeRepository()
        self.extractor = extractor or PdfPlumberExtractor()
        self.fragment_builder = fragment_builder or SemanticFragmentBuilder()
        self.clock = clock or (lambda: datetime.now(timezone.utc))

    def ingest(
        self,
        source: bytes | bytearray | memoryview | str | Path,
        *,
        original_filename: str | None = None,
        source_reference: str | None = None,
        declared_version: str | None = None,
        declared_date: date | str | None = None,
        corpus: Corpus | str = Corpus.NORMATIVE,
        review_status: str = "PENDING_REVIEW",
    ) -> IngestionResult:
        payload, detected_reference = _read_source(source)
        reference = source_reference or detected_reference
        filename = original_filename or _filename_from_reference(reference)
        corpus_value = Corpus(corpus)
        parsed_date = _parse_date(declared_date)
        extraction = self.extractor.extract(payload)
        extraction = extraction.model_copy(update={"source_reference": reference})
        content_sha = sha256_bytes(payload)
        document_id = make_document_id(filename, reference)
        now = self.clock()
        candidate_document = Document(
            document_id=document_id,
            original_filename=filename,
            corpus=corpus_value,
        )
        document = self.repository.register_document(candidate_document)
        version_id = make_document_version_id(
            document.document_id,
            content_sha,
            declared_version,
            parsed_date.isoformat() if parsed_date else None,
        )
        version = DocumentVersion(
            document_version_id=version_id,
            document_id=document.document_id,
            content_sha256=content_sha,
            declared_version=declared_version,
            declared_date=parsed_date,
            page_count=extraction.page_count,
            source_reference=reference,
            ingested_at=now,
            review_status=review_status,
        )
        fragments = self.fragment_builder.build(version, extraction)
        run_id = make_ingestion_run_id(corpus_value.value, version_id, extraction.extractor_version)
        run = IngestionRun(
            run_id=run_id,
            corpus=corpus_value,
            status="COMPLETED",
            source_count=1,
            document_version_ids=(version_id,),
            started_at=now,
            completed_at=self.clock(),
            warnings=tuple(
                extraction.warnings
                + tuple(warning for page in extraction.pages for warning in page.warnings)
                + tuple(
                    f"page {page.page_number}: quality flags={','.join(page.quality_flags)}"
                    for page in extraction.pages
                    if page.quality_flags
                )
            ),
        )
        version, fragments, run, idempotent = self.repository.store_version(version, fragments, run)
        snapshot = self.repository.publish_snapshot((version.document_version_id,), corpus_value)
        visible_fragments = self.repository.snapshot_fragments(snapshot.snapshot_id)
        return IngestionResult(
            document=document,
            document_version=version,
            fragments=visible_fragments,
            ingestion_run=run,
            snapshot=snapshot,
            idempotent=idempotent,
        )


def _parse_date(value: date | str | None) -> date | None:
    if value is None or isinstance(value, date):
        return value
    return date.fromisoformat(value)


def _filename_from_reference(reference: str) -> str:
    if reference == "<memory>":
        return "source.pdf"
    normalized = normalize_source_name(reference)
    return normalized.rsplit("/", 1)[-1] or "source.pdf"
