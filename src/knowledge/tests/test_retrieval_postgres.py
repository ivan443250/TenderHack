import os

import pytest
from fastapi.testclient import TestClient
from sqlalchemy.ext.asyncio import create_async_engine
from sqlalchemy import text

from tenderhack_knowledge.api import routes
from tenderhack_knowledge.contracts.v0 import Corpus, RetrieveRequest
from tenderhack_knowledge.ingestion.repository import UnknownSnapshotError
from tenderhack_knowledge.inference.errors import EmbeddingRevisionMismatchError
from tenderhack_knowledge.inference.model_refs import EMBEDDING_DIMENSION, EMBEDDING_MODEL_ID, EMBEDDING_REVISION
from tenderhack_knowledge.main import app
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository
from tenderhack_knowledge.retrieval import LexicalRetriever


SNAPSHOT_ID = "snap_0e979dfef376faff41f7c76416fda457"
TRACE_HEADERS = {
    "X-Trace-Id": "00000000-0000-4000-8000-000000000000",
    "X-Case-Id": "case-k2a",
    "X-Turn-Id": "turn-k2a",
}


def _database_url() -> str:
    return os.getenv("K2_TEST_DATABASE_URL", "")


def _request(query: str, *, corpus: Corpus = Corpus.NORMATIVE, snapshot_id: str | None = SNAPSHOT_ID, exact_codes: list[str] | None = None) -> RetrieveRequest:
    return RetrieveRequest(
        query=query,
        entities=[],
        exact_codes=exact_codes or [],
        corpus=corpus,
        snapshot_id=snapshot_id,
    )


@pytest.mark.asyncio
async def test_real_postgres_lexical_retrieval_and_snapshot_boundaries() -> None:
    url = _database_url()
    if not url:
        pytest.skip("K2_TEST_DATABASE_URL is not configured")

    engine = create_async_engine(url, pool_pre_ping=True)
    try:
        repository = PostgresKnowledgeRepository(engine)
        retriever = LexicalRetriever(repository)
        query = "\u041a\u0430\u043a \u043f\u043e\u0434\u043f\u0438\u0441\u0430\u0442\u044c \u0438 \u043e\u0442\u043f\u0440\u0430\u0432\u0438\u0442\u044c \u0423\u041f\u0414?"
        result = await retriever.retrieve(_request(query, exact_codes=["\u0423\u041f\u0414"]), allow_trigram=False)
        assert result.snapshot_id == SNAPSHOT_ID
        assert result.candidates
        assert result.records
        assert any("exact" in record.channels for record in result.records)

        typo = "\u0430\u043a\u0442\u0438\u0440\u043e\u0432\u0430\u0430\u043d\u0438\u0435"
        typo_result = await retriever.retrieve(_request(typo), allow_trigram=True)
        assert typo_result.candidates, "weak trigram recovery should return a candidate for an ordinary-word typo"
        assert any("trigram" in record.channels for record in typo_result.records)

        exact_code = await retriever.retrieve(_request("\u043d\u0430\u0439\u0442\u0438 \u0423\u041f\u0414-999999", exact_codes=["\u0423\u041f\u0414-999999"]), allow_trigram=True)
        assert all("trigram" not in record.channels for record in exact_code.records)

        with pytest.raises(UnknownSnapshotError):
            await retriever.retrieve(_request("test", snapshot_id="snap-does-not-exist"))
        with pytest.raises(UnknownSnapshotError):
            await retriever.retrieve(_request("test", corpus=Corpus.HISTORICAL, snapshot_id=None))

        # Parameterized FTS/LIKE inputs must treat SQL punctuation as data.
        injection = await repository.lexical_search(SNAPSHOT_ID, "' OR 1=1 --", ("%_",), allow_trigram=False)
        assert isinstance(injection, tuple)
    finally:
        await engine.dispose()


@pytest.mark.asyncio
async def test_real_postgres_dense_embedding_metadata_and_snapshot_boundaries() -> None:
    url = _database_url()
    if not url:
        pytest.skip("K2_TEST_DATABASE_URL is not configured")
    engine = create_async_engine(url, pool_pre_ping=True)
    fragment_id = ""
    try:
        repository = PostgresKnowledgeRepository(engine)
        fragments = await repository.snapshot_fragments(SNAPSHOT_ID)
        fragment_id = fragments[0].fragment_id
        vector = [1 / (EMBEDDING_DIMENSION**0.5)] * EMBEDDING_DIMENSION
        await repository.store_embeddings(
            SNAPSHOT_ID,
            {fragment_id: vector},
            model_id=EMBEDDING_MODEL_ID,
            model_revision=EMBEDDING_REVISION,
        )
        rows = await repository.dense_search(
            SNAPSHOT_ID,
            vector,
            model_id=EMBEDDING_MODEL_ID,
            model_revision=EMBEDDING_REVISION,
            limit=1,
        )
        assert rows and rows[0]["fragment_id"] == fragment_id
        with pytest.raises(EmbeddingRevisionMismatchError):
            await repository.dense_search(
                SNAPSHOT_ID,
                vector,
                model_id=EMBEDDING_MODEL_ID,
                model_revision="wrong-revision",
                limit=1,
            )
        with pytest.raises(UnknownSnapshotError):
            await repository.dense_search(
                "snap-does-not-exist",
                vector,
                model_id=EMBEDDING_MODEL_ID,
                model_revision=EMBEDDING_REVISION,
                limit=1,
            )
    finally:
        if fragment_id:
            async with engine.begin() as connection:
                await connection.execute(
                    text("DELETE FROM kb_fragment_embeddings WHERE fragment_id = :fragment_id"),
                    {"fragment_id": fragment_id},
                )
        await engine.dispose()


def test_real_retrieve_endpoint_uses_lexical_engine(monkeypatch) -> None:
    url = _database_url()
    if not url:
        pytest.skip("K2_TEST_DATABASE_URL is not configured")
    monkeypatch.setenv("DATABASE_URL", url)
    from tenderhack_knowledge.settings import config

    config.get_settings.cache_clear()
    routes.get_knowledge_repository.cache_clear()
    try:
        body = {
            "query": "\u041a\u0430\u043a \u043e\u0442\u043f\u0440\u0430\u0432\u0438\u0442\u044c \u0423\u041f\u0414?",
            "entities": [],
            "exact_codes": ["\u0423\u041f\u0414"],
            "corpus": "NORMATIVE",
            "snapshot_id": SNAPSHOT_ID,
        }
        with TestClient(app) as client:
            response = client.post("/v0/retrieve", json=body, headers=TRACE_HEADERS)
            unknown = {**body, "snapshot_id": "snap-does-not-exist"}
            missing = client.post("/v0/retrieve", json=unknown, headers=TRACE_HEADERS)
        assert response.status_code == 200, response.text
        payload = response.json()
        assert payload["snapshot_id"] == SNAPSHOT_ID
        assert payload["retrieval_config_version"] in {
            "lexical-v1",
            "hybrid-giga2048-querit-v2",
            "hybrid-giga2048-rrf-v2",
            "hybrid-giga2048-rrf-v2-no-dense",
            "hybrid-giga2048-rrf-v2-no-reranker",
        }
        assert payload["candidates"]
        assert missing.status_code == 404
        assert missing.json()["code"] == "UNKNOWN_SNAPSHOT"
    finally:
        routes.get_knowledge_repository.cache_clear()
        config.get_settings.cache_clear()
