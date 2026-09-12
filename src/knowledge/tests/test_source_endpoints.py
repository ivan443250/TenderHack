from datetime import datetime, timezone

from fastapi.testclient import TestClient

from tenderhack_knowledge.api import routes
from tenderhack_knowledge.ingestion.models import (
    Corpus,
    KnowledgeSnapshot,
    SourceAnchor,
    SourceResolution,
)
from tenderhack_knowledge.ingestion.repository import UnknownFragmentError
from tenderhack_knowledge.main import app


TRACE_HEADERS = {"X-Trace-Id": "00000000-0000-4000-8000-000000000000"}


class _FakeDurableRepository:
    def __init__(self) -> None:
        self.snapshot = KnowledgeSnapshot(
            snapshot_id="snap_real",
            corpus=Corpus.NORMATIVE,
            document_version_ids=("ver_real",),
            manifest_hash="f" * 64,
            created_at=datetime(2026, 9, 12, tzinfo=timezone.utc),
        )
        self.resolution = SourceResolution(
            fragment_id="frag_real",
            document_id="doc_real",
            document_version_id="ver_real",
            original_filename="Manual.pdf",
            source_reference="Manual.pdf",
            snapshot_id="snap_real",
            page_start=7,
            page_end=7,
            section="Section 2",
            kind="paragraph",
            heading_path=("Section 2",),
            text="Verified source fragment",
            source_anchor=SourceAnchor(
                page=7,
                bbox=(10, 20, 100, 40),
                section="Section 2",
                text_excerpt="Verified source fragment",
                text_hash="a" * 64,
            ),
            review_status="REVIEWED",
            declared_version="v11",
            page_count=93,
        )

    async def resolve_source(self, fragment_id: str, snapshot_id: str | None = None) -> SourceResolution:
        if fragment_id != self.resolution.fragment_id:
            raise UnknownFragmentError(fragment_id)
        return self.resolution.model_copy(update={"snapshot_id": snapshot_id or self.snapshot.snapshot_id})

    async def get_current_normative_snapshot(self) -> KnowledgeSnapshot:
        return self.snapshot

    async def snapshot_counts(self, snapshot_id: str) -> tuple[int, int]:
        assert snapshot_id == self.snapshot.snapshot_id
        return 6, 3210


def test_source_endpoint_uses_durable_repository_shape(monkeypatch) -> None:
    repository = _FakeDurableRepository()
    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: repository)
    response = TestClient(app).get("/v0/sources/frag_real", headers=TRACE_HEADERS)

    assert response.status_code == 200
    assert response.json() == {
        "document_id": "doc_real",
        "title": "Manual.pdf",
        "version": "v11",
        "page": 7,
        "anchor": "bbox:10.0,20.0,100.0,40.0",
        "text": "Verified source fragment",
        "snapshot_id": "snap_real",
    }
    assert "Manual.pdf" in response.json()["title"]
    assert "source_reference" not in response.json()


def test_source_endpoint_returns_contract_unknown_fragment(monkeypatch) -> None:
    repository = _FakeDurableRepository()
    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: repository)
    response = TestClient(app).get("/v0/sources/unknown", headers=TRACE_HEADERS)

    assert response.status_code == 404
    assert response.json()["code"] == "UNKNOWN_FRAGMENT"


def test_source_endpoint_does_not_expose_local_filename_path(monkeypatch) -> None:
    repository = _FakeDurableRepository()
    repository.resolution = repository.resolution.model_copy(
        update={"original_filename": r"C:\\private\\Manual.pdf"}
    )
    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: repository)

    response = TestClient(app).get("/v0/sources/frag_real", headers=TRACE_HEADERS)

    assert response.status_code == 200
    assert response.json()["title"] == "Manual.pdf"
    assert "private" not in response.text


def test_current_snapshot_endpoint_returns_real_counts(monkeypatch) -> None:
    repository = _FakeDurableRepository()
    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: repository)
    response = TestClient(app).get("/v0/snapshots/current", headers=TRACE_HEADERS)

    assert response.status_code == 200
    assert response.json() == {
        "snapshot_id": "snap_real",
        "created_at": "2026-09-12T00:00:00Z",
        "document_count": 6,
        "fragment_count": 3210,
    }
