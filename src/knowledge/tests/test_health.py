from fastapi.testclient import TestClient
import pytest

from tenderhack_knowledge.main import app
from tenderhack_knowledge.persistence.db import create_engine
from tenderhack_knowledge.settings.config import Settings, get_settings


HEADERS = {
    "X-Trace-Id": "00000000-0000-4000-8000-000000000000",
    "X-Case-Id": "case",
    "X-Turn-Id": "turn",
}


def test_live_health() -> None:
    response = TestClient(app).get("/health/live")
    assert response.status_code == 200
    assert response.json()["status"] == "ok"


def test_stub_never_emits_business_decision() -> None:
    response = TestClient(app).post(
        "/v0/answerability",
        headers=HEADERS,
        json={"query": "x", "snapshot_id": "s", "candidate_fragment_ids": []},
    )
    assert response.status_code == 200
    assert not {"Decision", "should_handoff", "HandoffStatus", "ResolutionStatus"}.intersection(response.json())


def test_orchestration_stage_requires_all_frozen_headers() -> None:
    response = TestClient(app).post(
        "/v0/understand",
        headers={"X-Trace-Id": HEADERS["X-Trace-Id"]},
        json={"text": "x"},
    )
    assert response.status_code == 400
    assert response.json()["code"] == "MISSING_TRACE_HEADER"


def test_settings_validation_and_unconfigured_db_boundary(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = Settings(database_url="postgresql+asyncpg://knowledge_rw:password@localhost/tenderhack")
    assert settings.database_url.startswith("postgresql+asyncpg://")
    monkeypatch.delenv("DATABASE_URL", raising=False)
    monkeypatch.delenv("KNOWLEDGE_DATABASE_URL", raising=False)
    get_settings.cache_clear()
    assert create_engine() is None
    get_settings.cache_clear()
