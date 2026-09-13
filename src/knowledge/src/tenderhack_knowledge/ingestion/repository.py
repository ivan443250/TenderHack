"""Knowledge-owned source repository and immutable snapshot boundary."""

from __future__ import annotations

from collections.abc import Iterable
from datetime import datetime, timezone
from typing import Protocol

from .ids import make_snapshot_id
from .models import (
    Document,
    DocumentVersion,
    IngestionRun,
    KnowledgeFragment,
    KnowledgeSnapshot,
    SourceResolution,
)


class SourceRepository(Protocol):
    def resolve_source(self, fragment_id: str, snapshot_id: str | None = None) -> SourceResolution: ...


class SourceResolutionError(LookupError):
    """Base error for source/snapshot resolution failures."""


class UnknownFragmentError(SourceResolutionError):
    pass


class UnknownSnapshotError(SourceResolutionError):
    pass


class UnknownDocumentError(SourceResolutionError):
    """E3 (docs/plans/active/2026-09-demo-readiness.md): a materials lookup for a document_id that
    is not part of the given snapshot — distinct from a document that exists but has no sections."""


class CorpusBoundaryError(ValueError):
    """Raised when normative and historical corpora are mixed."""


class InMemoryKnowledgeRepository(SourceRepository):
    """Deterministic repository used by ingestion tests and local tooling.

    It mirrors the persistence boundary without opening a database. Canonical
    fragments stay snapshot-neutral; every published snapshot receives an
    immutable projection with its own snapshot ID.
    """

    def __init__(self) -> None:
        self._documents: dict[str, Document] = {}
        self._versions: dict[str, DocumentVersion] = {}
        self._version_corpora: dict[str, str] = {}
        self._fragments: dict[str, KnowledgeFragment] = {}
        self._fragments_by_version: dict[str, tuple[str, ...]] = {}
        self._snapshots: dict[str, KnowledgeSnapshot] = {}
        self._snapshot_fragments: dict[str, dict[str, KnowledgeFragment]] = {}
        self._runs: dict[str, IngestionRun] = {}

    def register_document(self, document: Document) -> Document:
        existing = self._documents.get(document.document_id)
        if existing is None:
            self._documents[document.document_id] = document
            return document
        if existing.corpus != document.corpus or existing.original_filename != document.original_filename:
            raise ValueError(f"document identity collision for {document.document_id}")
        return existing

    def store_version(
        self,
        version: DocumentVersion,
        fragments: Iterable[KnowledgeFragment],
        run: IngestionRun,
    ) -> tuple[DocumentVersion, tuple[KnowledgeFragment, ...], IngestionRun, bool]:
        existing = self._versions.get(version.document_version_id)
        if existing is not None:
            if existing.content_sha256 != version.content_sha256:
                raise ValueError(f"document version collision for {version.document_version_id}")
            ids = self._fragments_by_version.get(version.document_version_id, ())
            return (
                existing,
                tuple(self._fragments[fragment_id] for fragment_id in ids),
                self._runs[run.run_id],
                True,
            )
        fragment_values = tuple(fragments)
        if any(fragment.document_version_id != version.document_version_id for fragment in fragment_values):
            raise ValueError("fragment belongs to a different document version")
        self._versions[version.document_version_id] = version
        self._version_corpora[version.document_version_id] = self._documents[version.document_id].corpus.value
        ids: list[str] = []
        for fragment in fragment_values:
            if fragment.fragment_id in self._fragments and self._fragments[fragment.fragment_id] != fragment:
                raise ValueError(f"fragment identity collision for {fragment.fragment_id}")
            self._fragments[fragment.fragment_id] = fragment
            ids.append(fragment.fragment_id)
        self._fragments_by_version[version.document_version_id] = tuple(ids)
        self._runs[run.run_id] = run
        return version, fragment_values, run, False

    def publish_snapshot(self, document_version_ids: Iterable[str], corpus: str | object) -> KnowledgeSnapshot:
        corpus_value = getattr(corpus, "value", corpus)
        corpus_value = str(corpus_value)
        version_ids = tuple(sorted(set(document_version_ids)))
        for version_id in version_ids:
            if version_id not in self._versions:
                raise UnknownSnapshotError(f"unknown document version {version_id}")
            if self._version_corpora[version_id] != corpus_value:
                raise CorpusBoundaryError(
                    f"cannot publish {self._version_corpora[version_id]} document version in {corpus_value} snapshot"
                )
        snapshot_id, manifest_hash = make_snapshot_id(corpus_value, version_ids)
        existing = self._snapshots.get(snapshot_id)
        if existing is not None:
            return existing
        snapshot = KnowledgeSnapshot(
            snapshot_id=snapshot_id,
            corpus=corpus_value,
            document_version_ids=version_ids,
            manifest_hash=manifest_hash,
            created_at=datetime.now(timezone.utc),
            review_status="PENDING_REVIEW",
        )
        projections: dict[str, KnowledgeFragment] = {}
        for version_id in version_ids:
            for fragment_id in self._fragments_by_version.get(version_id, ()):
                fragment = self._fragments[fragment_id]
                projections[fragment_id] = fragment.model_copy(update={"snapshot_id": snapshot_id})
        self._snapshots[snapshot_id] = snapshot
        self._snapshot_fragments[snapshot_id] = projections
        return snapshot

    def snapshot_fragments(self, snapshot_id: str) -> tuple[KnowledgeFragment, ...]:
        try:
            values = self._snapshot_fragments[snapshot_id]
        except KeyError as exc:
            raise UnknownSnapshotError(snapshot_id) from exc
        return tuple(values.values())

    def get_document(self, document_id: str) -> Document | None:
        return self._documents.get(document_id)

    def get_version(self, document_version_id: str) -> DocumentVersion | None:
        return self._versions.get(document_version_id)

    def get_run(self, run_id: str) -> IngestionRun | None:
        return self._runs.get(run_id)

    def get_snapshot(self, snapshot_id: str) -> KnowledgeSnapshot | None:
        return self._snapshots.get(snapshot_id)

    def get_current_normative_snapshot(self) -> KnowledgeSnapshot | None:
        normative = [s for s in self._snapshots.values() if getattr(s.corpus, "value", s.corpus) == "NORMATIVE"]
        if not normative:
            return None
        return max(normative, key=lambda s: (s.created_at, s.snapshot_id))

    def list_snapshot_materials(self, snapshot_id: str) -> tuple[dict, ...]:
        """E3: one row per document in the snapshot — `document_id`/`original_filename`/
        `declared_version`/`declared_date`/`page_count` from its version, `fragment_count` from
        the snapshot's own fragment projection (not the canonical version's full fragment set)."""

        if snapshot_id not in self._snapshots:
            raise UnknownSnapshotError(snapshot_id)
        snapshot = self._snapshots[snapshot_id]
        fragment_counts: dict[str, int] = {}
        for fragment in self._snapshot_fragments[snapshot_id].values():
            fragment_counts[fragment.document_version_id] = fragment_counts.get(fragment.document_version_id, 0) + 1
        rows = []
        for version_id in snapshot.document_version_ids:
            version = self._versions[version_id]
            document = self._documents[version.document_id]
            rows.append(
                {
                    "document_id": document.document_id,
                    "original_filename": document.original_filename,
                    "declared_version": version.declared_version,
                    "declared_date": version.declared_date,
                    "page_count": version.page_count,
                    "fragment_count": fragment_counts.get(version_id, 0),
                }
            )
        rows.sort(key=lambda row: row["original_filename"])
        return tuple(rows)

    def list_document_sections(self, snapshot_id: str, document_id: str) -> tuple[dict, ...]:
        if snapshot_id not in self._snapshots:
            raise UnknownSnapshotError(snapshot_id)
        snapshot = self._snapshots[snapshot_id]
        version_ids = {vid for vid in snapshot.document_version_ids if self._versions[vid].document_id == document_id}
        if not version_ids:
            raise UnknownDocumentError(document_id)

        by_section: dict[str | None, list[KnowledgeFragment]] = {}
        for fragment in self._snapshot_fragments[snapshot_id].values():
            if fragment.document_version_id in version_ids:
                by_section.setdefault(fragment.section, []).append(fragment)

        rows = []
        for section, fragments in by_section.items():
            ordered = sorted(fragments, key=lambda f: (f.page_start, f.fragment_id))
            rows.append(
                {
                    "section": section,
                    "page_start": min(f.page_start for f in fragments),
                    "page_end": max(f.page_end for f in fragments),
                    "first_fragment_id": ordered[0].fragment_id,
                }
            )
        # "Без раздела" (section is None) sorts last, per document page order otherwise.
        rows.sort(key=lambda row: (row["section"] is None, row["page_start"]))
        return tuple(rows)

    def resolve_source(self, fragment_id: str, snapshot_id: str | None = None) -> SourceResolution:
        if snapshot_id is None:
            fragment = self._fragments.get(fragment_id)
        else:
            if snapshot_id not in self._snapshots:
                raise UnknownSnapshotError(snapshot_id)
            fragment = self._snapshot_fragments[snapshot_id].get(fragment_id)
        if fragment is None:
            raise UnknownFragmentError(fragment_id)
        version = self._versions[fragment.document_version_id]
        document = self._documents[version.document_id]
        return SourceResolution(
            fragment_id=fragment.fragment_id,
            document_id=document.document_id,
            document_version_id=version.document_version_id,
            original_filename=document.original_filename,
            source_reference=version.source_reference,
            snapshot_id=fragment.snapshot_id,
            page_start=fragment.page_start,
            page_end=fragment.page_end,
            section=fragment.section,
            kind=fragment.kind,
            heading_path=fragment.heading_path,
            text=fragment.text,
            source_anchor=fragment.source_anchor,
            review_status=fragment.review_status,
            declared_version=version.declared_version,
            declared_date=version.declared_date,
            page_count=version.page_count,
        )


class SourceResolver:
    """Future HTTP source endpoint boundary backed by a repository."""

    def __init__(self, repository: SourceRepository) -> None:
        self.repository = repository

    def resolve(self, fragment_id: str, snapshot_id: str | None = None) -> SourceResolution:
        return self.repository.resolve_source(fragment_id, snapshot_id)
