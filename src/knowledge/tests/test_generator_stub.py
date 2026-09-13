"""infra/inference/stub/app.py is a test double, not shipped in this package.

These tests import it directly from the repo tree and exercise it exactly the
way `LocalOpenAIChatGenerator` does (POST /v1/chat/completions with a prompt
built by `build_draft_prompt`), then run its response through the same
parse -> verify pipeline `GroundedDraftService` uses, to guarantee the stub's
claims are genuinely `verify_v1`-supported and not merely well-formed JSON.
"""

from __future__ import annotations

import importlib.util
import sys
from datetime import datetime, timezone
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from tenderhack_knowledge.contracts.v0 import Corpus, DraftConstraints
from tenderhack_knowledge.generation.models import parse_model_output
from tenderhack_knowledge.ingestion.models import KnowledgeFragment, KnowledgeSnapshot, SourceAnchor
from tenderhack_knowledge.inference.prompts import build_draft_prompt
from tenderhack_knowledge.verification import verify_claims

REPO_ROOT = Path(__file__).resolve().parents[3]
STUB_APP_PATH = REPO_ROOT / "infra" / "inference" / "stub" / "app.py"
SNAPSHOT_ID = "snap-stub"


def _load_stub_app():
    spec = importlib.util.spec_from_file_location("generator_stub_app", STUB_APP_PATH)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module.app


def _fragment(fragment_id: str, text: str) -> KnowledgeFragment:
    return KnowledgeFragment(
        fragment_id=fragment_id,
        document_version_id=f"version-{SNAPSHOT_ID}",
        snapshot_id=SNAPSHOT_ID,
        page_start=3,
        page_end=3,
        section="Procedure",
        kind="text",
        text=text,
        source_anchor=SourceAnchor(page=3, section="Procedure", text_hash=f"hash-{fragment_id}"),
        review_status="VERIFIED",
        text_hash=f"hash-{fragment_id}",
    )


class Repository:
    def __init__(self, fragments: tuple[KnowledgeFragment, ...]) -> None:
        self.fragments = fragments
        self.snapshot = KnowledgeSnapshot(
            snapshot_id=SNAPSHOT_ID,
            corpus=Corpus.NORMATIVE,
            document_version_ids=(f"version-{SNAPSHOT_ID}",),
            manifest_hash="manifest-stub",
            created_at=datetime.now(timezone.utc),
            review_status="VERIFIED",
        )

    def get_snapshot(self, snapshot_id: str) -> KnowledgeSnapshot | None:
        return self.snapshot if snapshot_id == SNAPSHOT_ID else None

    def snapshot_fragments(self, snapshot_id: str) -> tuple[KnowledgeFragment, ...]:
        return self.fragments if snapshot_id == SNAPSHOT_ID else ()


@pytest.mark.asyncio
async def test_stub_claims_pass_deterministic_verification() -> None:
    fragments = (
        _fragment("frag-offer-1", "Чтобы создать оферту, откройте раздел Оферты и нажмите кнопку Создать. Заполните обязательные поля и сохраните черновик."),
        _fragment("frag-offer-2", "Оферта переходит в статус На согласовании после отправки покупателю."),
    )
    prompt = build_draft_prompt(
        "как создать оферту",
        fragments,
        DraftConstraints(max_output_tokens=800, tone="neutral"),
    )
    client = TestClient(_load_stub_app())

    response = client.post(
        "/v1/chat/completions",
        json={"model": "stub-extractive-v0", "messages": [{"role": "user", "content": prompt}]},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["model"] == "stub-extractive-v0"
    content = body["choices"][0]["message"]["content"]

    draft = parse_model_output(content)
    assert draft.claims
    assert draft.draft_markdown.strip()

    repository = Repository(fragments)
    checks = await verify_claims(SNAPSHOT_ID, draft.claims, repository)
    assert checks
    assert all(check.result.supported for check in checks), [check.reasons for check in checks]


def test_stub_rejects_prompt_without_input_data_marker() -> None:
    client = TestClient(_load_stub_app())

    response = client.post(
        "/v1/chat/completions",
        json={"model": "stub-extractive-v0", "messages": [{"role": "user", "content": "no marker here"}]},
    )

    assert response.status_code == 400


def test_stub_health() -> None:
    client = TestClient(_load_stub_app())

    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "ok", "model": "stub-extractive-v0"}
