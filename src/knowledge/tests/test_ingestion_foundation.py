from __future__ import annotations

import re
from datetime import datetime, timezone
from pathlib import Path

import pytest

from tenderhack_knowledge.ingestion import (
    Corpus,
    CorpusBoundaryError,
    DocumentVersion,
    ExtractionError,
    ExtractionResult,
    InMemoryKnowledgeRepository,
    IngestionPipeline,
    PageExtraction,
    PdfPlumberExtractor,
    SemanticFragmentBuilder,
    SourceResolver,
    TextBlock,
    UnknownFragmentError,
)


def _technical_pdf(page_lines: list[list[str]]) -> bytes:
    """Create a tiny text PDF with Unicode ToUnicode mapping and a ruled table."""

    strings = [text for page in page_lines for text in page]
    strings.extend(["Name", "Status", "Document", "Ready"])
    chars = sorted({character for text in strings for character in text})
    cmap_lines = [
        "/CIDInit /ProcSet findresource begin",
        "12 dict begin begincmap",
        "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def",
        "/CMapName /Adobe-Identity-UCS def",
        "/CMapType 2 def",
        "1 begincodespacerange",
        "<0000><FFFF>",
        "endcodespacerange",
        f"{len(chars)} beginbfchar",
    ]
    cmap_lines.extend(f"<{ord(character):04X}><{ord(character):04X}>" for character in chars)
    cmap_lines.extend(["endbfchar", "endcmap CMapName currentdict /CMap defineresource pop end end"])
    cmap = "\n".join(cmap_lines).encode("ascii")

    objects: dict[int, bytes] = {
        1: b"<< /Type /Catalog /Pages 2 0 R >>",
        3: b"<< /Type /Font /Subtype /Type0 /BaseFont /Helvetica /Encoding /Identity-H /DescendantFonts [4 0 R] /ToUnicode 6 0 R >>",
        4: b"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Helvetica /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 5 0 R /DW 600 >>",
        5: b"<< /Type /FontDescriptor /FontName /Helvetica /Flags 4 /FontBBox [0 -200 1000 900] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 >>",
        6: b"<< /Length " + str(len(cmap)).encode("ascii") + b" >>\nstream\n" + cmap + b"\nendstream",
    }
    page_refs: list[int] = []
    next_object = 7
    for page_index, lines in enumerate(page_lines):
        page_object = next_object
        content_object = next_object + 1
        next_object += 2
        page_refs.append(page_object)
        commands = ["BT", "/F1 12 Tf", "50 760 Td"]
        for index, line in enumerate(lines):
            if index:
                commands.append("0 -18 Td")
            commands.append(f"<{''.join(f'{ord(character):04X}' for character in line)}> Tj")
        commands.append("ET")
        if page_index == 0:
            commands.extend(
                [
                    "50 560 m 350 560 l S",
                    "50 520 m 350 520 l S",
                    "50 480 m 350 480 l S",
                    "50 440 m 350 440 l S",
                    "50 560 m 50 440 l S",
                    "200 560 m 200 440 l S",
                    "350 560 m 350 440 l S",
                    "BT /F1 10 Tf 55 535 Td <004E0061006D0065> Tj 145 0 Td <005300740061007400750073> Tj ET",
                    "BT /F1 10 Tf 55 495 Td <0044006F00630075006D0065006E0074> Tj 145 0 Td <00520065006100640079> Tj ET",
                ]
            )
        content = "\n".join(commands).encode("ascii")
        objects[page_object] = (
            f"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 800] /Resources << /Font << /F1 3 0 R >> >> /Contents {content_object} 0 R >>".encode("ascii")
        )
        objects[content_object] = b"<< /Length " + str(len(content)).encode("ascii") + b" >>\nstream\n" + content + b"\nendstream"
    objects[2] = f"<< /Type /Pages /Kids [{' '.join(f'{ref} 0 R' for ref in page_refs)}] /Count {len(page_refs)} >>".encode("ascii")

    output = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = [0]
    for object_id in sorted(objects):
        offsets.append(len(output))
        output.extend(f"{object_id} 0 obj\n".encode("ascii"))
        output.extend(objects[object_id])
        output.extend(b"\nendobj\n")
    xref_offset = len(output)
    output.extend(f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode("ascii"))
    for object_id in sorted(objects):
        output.extend(f"{offsets[object_id]:010d} 00000 n \n".encode("ascii"))
    output.extend(f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref_offset}\n%%EOF\n".encode("ascii"))
    return bytes(output)


@pytest.fixture
def technical_pdf() -> bytes:
    return _technical_pdf(
        [
            ["1 Процедура подачи", "Русский текст инструкции сохраняется.", "1. Подготовить документ."],
            [],
        ]
    )


def test_pdf_extractor_preserves_unicode_pages_tables_and_quality_flags(technical_pdf: bytes) -> None:
    result = PdfPlumberExtractor().extract(technical_pdf)
    assert result.page_count == 2
    assert "Русский" in result.pages[0].text
    assert result.pages[1].quality_flags == ("EMPTY_PAGE",)
    assert result.pages[0].text_blocks
    assert result.pages[0].tables
    assert result.pages[0].tables[0].headers == ("Name", "Status")
    assert result.pages[0].tables[0].rows[0] == ("Document", "Ready")
    assert result.pages[0].text_blocks[0].bbox is not None


def test_same_pdf_is_idempotent_and_source_resolves(technical_pdf: bytes) -> None:
    repository = InMemoryKnowledgeRepository()
    pipeline = IngestionPipeline(repository)
    first = pipeline.ingest(
        technical_pdf,
        original_filename="Инструкция тест.pdf",
        source_reference="fixtures/Инструкция тест.pdf",
        declared_version="v1",
    )
    second = pipeline.ingest(
        technical_pdf,
        original_filename="Инструкция тест.pdf",
        source_reference="fixtures/Инструкция тест.pdf",
        declared_version="v1",
    )
    assert first.document_version.document_version_id == second.document_version.document_version_id
    assert first.document_version.content_sha256 == second.document_version.content_sha256
    assert [fragment.fragment_id for fragment in first.fragments] == [fragment.fragment_id for fragment in second.fragments]
    assert first.snapshot.snapshot_id == second.snapshot.snapshot_id
    assert second.idempotent is True
    assert any("EMPTY_PAGE" in warning for warning in first.ingestion_run.warnings)
    table_fragments = [fragment for fragment in first.fragments if fragment.kind == "table_row"]
    assert table_fragments
    assert "Name | Status" in table_fragments[0].text
    assert table_fragments[0].source_anchor.page == 1
    resolved = SourceResolver(repository).resolve(first.fragments[0].fragment_id, first.snapshot.snapshot_id)
    assert resolved.original_filename == "Инструкция тест.pdf"
    assert resolved.page_start == 1
    assert resolved.source_anchor.text_hash == first.fragments[0].text_hash


def test_changed_bytes_create_new_version_and_old_snapshot_stays_immutable(technical_pdf: bytes) -> None:
    repository = InMemoryKnowledgeRepository()
    pipeline = IngestionPipeline(repository)
    first = pipeline.ingest(technical_pdf, original_filename="guide.pdf", source_reference="fixture/guide.pdf")
    changed = pipeline.ingest(
        _technical_pdf([["1 Процедура подачи", "Изменённый текст."], []]),
        original_filename="guide.pdf",
        source_reference="fixture/guide.pdf",
    )
    assert first.document_version.document_version_id != changed.document_version.document_version_id
    assert first.document_version.content_sha256 != changed.document_version.content_sha256
    assert first.snapshot.snapshot_id != changed.snapshot.snapshot_id
    assert repository.get_snapshot(first.snapshot.snapshot_id).document_version_ids == (
        first.document_version.document_version_id,
    )
    old_text = SourceResolver(repository).resolve(first.fragments[0].fragment_id, first.snapshot.snapshot_id).text
    assert "Русский" in old_text


def test_source_button_provenance_is_deterministic_across_version_publication(technical_pdf: bytes) -> None:
    repository = InMemoryKnowledgeRepository()
    pipeline = IngestionPipeline(repository)
    first = pipeline.ingest(
        technical_pdf,
        original_filename="Организаторская инструкция.pdf",
        source_reference="organizer/Организаторская инструкция.pdf",
        declared_version="v1",
    )
    stored = next(
        fragment
        for fragment in repository.snapshot_fragments(first.snapshot.snapshot_id)
        if fragment.kind == "table_row"
    )
    version = repository.get_version(stored.document_version_id)
    assert version is not None
    assert version.document_version_id == first.document_version.document_version_id

    resolver = SourceResolver(repository)
    resolved_once = resolver.resolve(stored.fragment_id, first.snapshot.snapshot_id)
    resolved_twice = resolver.resolve(stored.fragment_id, first.snapshot.snapshot_id)
    assert resolved_once.model_dump(mode="json") == resolved_twice.model_dump(mode="json")
    assert resolved_once.fragment_id == stored.fragment_id
    assert resolved_once.document_version_id == version.document_version_id
    assert resolved_once.original_filename == "Организаторская инструкция.pdf"
    assert resolved_once.source_reference == "organizer/Организаторская инструкция.pdf"
    assert resolved_once.page_start == resolved_once.page_end == 1
    assert resolved_once.source_anchor.page == 1
    assert resolved_once.source_anchor.text_hash == stored.text_hash
    assert resolved_once.source_anchor.bbox is not None

    changed = pipeline.ingest(
        _technical_pdf([["1 Процедура", "changed source"], []]),
        original_filename="Организаторская инструкция.pdf",
        source_reference="organizer/Организаторская инструкция.pdf",
        declared_version="v2",
    )
    resolved_after_update = resolver.resolve(stored.fragment_id, first.snapshot.snapshot_id)
    assert resolved_after_update.model_dump(mode="json") == resolved_once.model_dump(mode="json")
    with pytest.raises(UnknownFragmentError):
        resolver.resolve(stored.fragment_id, changed.snapshot.snapshot_id)


def test_duplicate_text_on_one_page_keeps_distinct_source_bboxes() -> None:
    version = DocumentVersion(
        document_version_id="ver_duplicate",
        document_id="doc_duplicate",
        content_sha256="a" * 64,
        page_count=1,
        source_reference="fixture.pdf",
        ingested_at=datetime.now(timezone.utc),
    )
    extraction = ExtractionResult(
        source_reference="fixture.pdf",
        page_count=1,
        pages=(
            PageExtraction(
                page_number=1,
                text="Repeated line\n\nRepeated line",
                text_blocks=(
                    TextBlock(text="Repeated line", bbox=(10, 10, 100, 20)),
                    TextBlock(text="Repeated line", bbox=(10, 40, 100, 50)),
                ),
            ),
        ),
    )
    fragments = SemanticFragmentBuilder(max_embedding_tokens=1).build(version, extraction)
    repeated = [fragment for fragment in fragments if fragment.text == "Repeated line"]
    assert len(repeated) == 2
    assert repeated[0].fragment_id != repeated[1].fragment_id
    assert repeated[0].source_anchor.bbox == (10.0, 10.0, 100.0, 20.0)
    assert repeated[1].source_anchor.bbox == (10.0, 40.0, 100.0, 50.0)


def test_duplicate_text_on_different_pages_keeps_distinct_fragment_ids() -> None:
    version = DocumentVersion(
        document_version_id="ver_pages",
        document_id="doc_pages",
        content_sha256="d" * 64,
        page_count=2,
        source_reference="fixture.pdf",
        ingested_at=datetime.now(timezone.utc),
    )
    extraction = ExtractionResult(
        source_reference="fixture.pdf",
        page_count=2,
        pages=(
            PageExtraction(page_number=1, text="Repeated evidence"),
            PageExtraction(page_number=2, text="Repeated evidence"),
        ),
    )
    fragments = SemanticFragmentBuilder(max_embedding_tokens=1).build(version, extraction)
    assert len(fragments) == 2
    assert fragments[0].text_hash == fragments[1].text_hash
    assert fragments[0].fragment_id != fragments[1].fragment_id
    assert (fragments[0].page_start, fragments[1].page_start) == (1, 2)


def test_long_condition_is_kept_as_one_semantic_fragment() -> None:
    version = DocumentVersion(
        document_version_id="ver_condition",
        document_id="doc_condition",
        content_sha256="b" * 64,
        page_count=1,
        source_reference="fixture.pdf",
        ingested_at=datetime.now(timezone.utc),
    )
    condition = "if " + ("applicable condition " * 500)
    extraction = ExtractionResult(
        source_reference="fixture.pdf",
        page_count=1,
        pages=(PageExtraction(page_number=1, text=condition),),
    )
    fragments = SemanticFragmentBuilder(max_embedding_tokens=8).build(version, extraction)
    assert len(fragments) == 1
    assert fragments[0].kind == "condition"
    assert fragments[0].text == condition.strip()


def test_corrupted_pdf_is_rejected_without_partial_publication() -> None:
    with pytest.raises(ExtractionError):
        PdfPlumberExtractor().extract(b"not a PDF")


def test_source_anchor_without_bbox_remains_resolvable() -> None:
    version = DocumentVersion(
        document_version_id="ver_no_bbox",
        document_id="doc_no_bbox",
        content_sha256="c" * 64,
        page_count=1,
        source_reference="fixture.pdf",
        ingested_at=datetime.now(timezone.utc),
    )
    extraction = ExtractionResult(
        source_reference="fixture.pdf",
        page_count=1,
        pages=(
            PageExtraction(
                page_number=1,
                text="Text without geometry",
                text_blocks=(TextBlock(text="Text without geometry"),),
            ),
        ),
    )
    fragment = SemanticFragmentBuilder().build(version, extraction)[0]
    assert fragment.source_anchor.bbox is None
    assert fragment.source_anchor.page == 1


def test_same_content_from_different_safe_paths_keeps_identity(technical_pdf: bytes, tmp_path: Path) -> None:
    repository = InMemoryKnowledgeRepository()
    pipeline = IngestionPipeline(repository)
    first_path = tmp_path / "one" / "Русский.pdf"
    second_path = tmp_path / "two" / "Русский.pdf"
    first_path.parent.mkdir()
    second_path.parent.mkdir()
    first_path.write_bytes(technical_pdf)
    second_path.write_bytes(technical_pdf)
    first = pipeline.ingest(first_path)
    second = pipeline.ingest(second_path)
    assert first.document_version.document_version_id == second.document_version.document_version_id
    assert [fragment.fragment_id for fragment in first.fragments] == [fragment.fragment_id for fragment in second.fragments]


def test_windows_and_posix_source_references_derive_same_filename(technical_pdf: bytes) -> None:
    repository = InMemoryKnowledgeRepository()
    pipeline = IngestionPipeline(repository)
    windows = pipeline.ingest(
        technical_pdf,
        source_reference="C:\\fixtures\\Русский.pdf",
    )
    posix = pipeline.ingest(
        technical_pdf,
        source_reference="C:/fixtures/Русский.pdf",
    )
    assert windows.document.original_filename == "Русский.pdf"
    assert windows.document.document_id == posix.document.document_id
    assert windows.document_version.document_version_id == posix.document_version.document_version_id


def test_historical_version_cannot_be_published_as_normative(technical_pdf: bytes) -> None:
    repository = InMemoryKnowledgeRepository()
    pipeline = IngestionPipeline(repository)
    historical = pipeline.ingest(technical_pdf, original_filename="history.pdf", corpus=Corpus.HISTORICAL)
    with pytest.raises(CorpusBoundaryError):
        repository.publish_snapshot([historical.document_version.document_version_id], Corpus.NORMATIVE)


def test_migration_mentions_only_knowledge_owned_tables() -> None:
    migration = Path(__file__).parents[1] / "migrations" / "versions" / "0002_ingestion_provenance.py"
    source = migration.read_text(encoding="utf-8")
    assert not re.search(r"\b(?:cases|messages|turns|api_jobs|outbox|feedback)\b", source)
    assert "kb_documents" in source
    assert "kb_fragments" in source
    assert "kb_snapshot_fragments" in source
