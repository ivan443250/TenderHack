import os
from datetime import datetime, timezone

import pytest
from sqlalchemy.ext.asyncio import create_async_engine

from tenderhack_knowledge.ingestion.models import (
    Corpus,
    Document,
    DocumentVersion,
    IngestionRun,
    KnowledgeFragment,
    SourceAnchor,
)
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository
from tenderhack_knowledge.persistence.repository import _same_version_facts


@pytest.mark.asyncio
async def test_postgres_repository_roundtrip_survives_recreation() -> None:
    url = os.getenv("K1C_TEST_DATABASE_URL")
    if not url:
        pytest.skip("K1C_TEST_DATABASE_URL is not configured; durable DB gate is environment-blocked")

    engine = create_async_engine(url, pool_pre_ping=True)
    try:
        repository = PostgresKnowledgeRepository(engine)
        now = datetime.now(timezone.utc)
        document = Document(document_id="doc_db_test", original_filename="db-test.pdf", corpus=Corpus.NORMATIVE)
        version = DocumentVersion(
            document_version_id="ver_db_test",
            document_id=document.document_id,
            content_sha256="a" * 64,
            declared_version="test",
            page_count=1,
            source_reference="db-test.pdf",
            ingested_at=now,
        )
        fragment = KnowledgeFragment(
            fragment_id="frag_db_test",
            document_version_id=version.document_version_id,
            page_start=1,
            page_end=1,
            kind="paragraph",
            text="durable fragment",
            source_anchor=SourceAnchor(page=1, text_hash="b" * 64),
            text_hash="b" * 64,
        )
        run = IngestionRun(
            run_id="run_db_test",
            corpus=Corpus.NORMATIVE,
            status="COMPLETED",
            source_count=1,
            document_version_ids=(version.document_version_id,),
            started_at=now,
            completed_at=now,
        )
        await repository.register_document(document)
        await repository.store_version(version, (fragment,), run)
        snapshot = await repository.publish_snapshot((version.document_version_id,), Corpus.NORMATIVE)

        recreated = PostgresKnowledgeRepository(engine)
        resolved = await recreated.resolve_source(fragment.fragment_id, snapshot.snapshot_id)
        assert resolved.document_id == document.document_id
        assert resolved.document_version_id == version.document_version_id
        assert resolved.text == fragment.text
        assert (await recreated.snapshot_counts(snapshot.snapshot_id)) == (1, 1)
    finally:
        await engine.dispose()


def test_version_rerun_identity_ignores_ingestion_timestamp() -> None:
    now = datetime.now(timezone.utc)
    base = DocumentVersion(
        document_version_id="ver_rerun",
        document_id="doc_rerun",
        content_sha256="a" * 64,
        declared_version="v1",
        page_count=1,
        source_reference="rerun.pdf",
        ingested_at=now,
    )
    rerun = base.model_copy(update={"ingested_at": now.replace(microsecond=0)})

    assert _same_version_facts(base, rerun)
