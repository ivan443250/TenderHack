"""E3 (docs/plans/active/2026-09-demo-readiness.md): GET /v0/materials and
GET /v0/materials/{document_id}/sections. Fake repository mirrors test_source_endpoints.py's
`_FakeDurableRepository` shape — the routes always `await` repository calls, so the fake's methods
must be `async def` even though `InMemoryKnowledgeRepository` (a sync-only test double used
elsewhere) is not."""

from datetime import date, datetime, timezone

from fastapi.testclient import TestClient

from tenderhack_knowledge.api import routes
from tenderhack_knowledge.ingestion.models import Corpus, KnowledgeSnapshot
from tenderhack_knowledge.ingestion.repository import UnknownDocumentError
from tenderhack_knowledge.main import app

TRACE_HEADERS = {"X-Trace-Id": "00000000-0000-4000-8000-000000000000"}


class _FakeMaterialsRepository:
    def __init__(self) -> None:
        self.snapshot = KnowledgeSnapshot(
            snapshot_id="snap_real",
            corpus=Corpus.NORMATIVE,
            document_version_ids=("ver_a", "ver_b"),
            manifest_hash="f" * 64,
            created_at=datetime(2026, 9, 12, tzinfo=timezone.utc),
        )
        self._materials = [
            {
                "document_id": "doc_a",
                "original_filename": "Инструкция для поставщика.pdf",
                "declared_version": "v11",
                "declared_date": date(2025, 3, 1),
                "page_count": 93,
                "fragment_count": 1200,
            },
            {
                "document_id": "doc_b",
                "original_filename": "Регламент ЭДО.pdf",
                "declared_version": None,
                "declared_date": None,
                "page_count": 40,
                "fragment_count": 300,
            },
        ]
        self._sections = {
            "doc_a": [
                {"section": "1. Общие положения", "page_start": 1, "page_end": 5, "first_fragment_id": "frag_1"},
                {"section": None, "page_start": 90, "page_end": 93, "first_fragment_id": "frag_9"},
            ]
        }

    async def get_current_normative_snapshot(self) -> KnowledgeSnapshot:
        return self.snapshot

    async def list_snapshot_materials(self, snapshot_id: str) -> tuple[dict, ...]:
        assert snapshot_id == self.snapshot.snapshot_id
        return tuple(self._materials)

    async def list_document_sections(self, snapshot_id: str, document_id: str) -> tuple[dict, ...]:
        assert snapshot_id == self.snapshot.snapshot_id
        if document_id not in self._sections:
            raise UnknownDocumentError(document_id)
        return tuple(self._sections[document_id])


def test_materials_endpoint_lists_documents_in_the_current_snapshot(monkeypatch) -> None:
    repository = _FakeMaterialsRepository()
    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: repository)

    response = TestClient(app).get("/v0/materials", headers=TRACE_HEADERS)

    assert response.status_code == 200
    body = response.json()
    assert body["snapshot_id"] == "snap_real"
    assert [m["document_id"] for m in body["materials"]] == ["doc_a", "doc_b"]
    assert body["materials"][0] == {
        "document_id": "doc_a",
        "title": "Инструкция для поставщика.pdf",
        "declared_version": "v11",
        "declared_date": "2025-03-01",
        "page_count": 93,
        "fragment_count": 1200,
    }
    assert body["materials"][1]["declared_version"] is None
    assert body["materials"][1]["declared_date"] is None


def test_material_sections_are_ordered_by_page_with_unsectioned_last(monkeypatch) -> None:
    repository = _FakeMaterialsRepository()
    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: repository)

    response = TestClient(app).get("/v0/materials/doc_a/sections", headers=TRACE_HEADERS)

    assert response.status_code == 200
    body = response.json()
    assert body["snapshot_id"] == "snap_real"
    assert body["document_id"] == "doc_a"
    assert [s["section"] for s in body["sections"]] == ["1. Общие положения", None]
    assert body["sections"][0]["first_fragment_id"] == "frag_1"


def test_unknown_document_returns_404(monkeypatch) -> None:
    repository = _FakeMaterialsRepository()
    monkeypatch.setattr(routes, "get_knowledge_repository", lambda: repository)

    response = TestClient(app).get("/v0/materials/doc_unknown/sections", headers=TRACE_HEADERS)

    assert response.status_code == 404
    assert response.json()["code"] == "UNKNOWN_DOCUMENT"
