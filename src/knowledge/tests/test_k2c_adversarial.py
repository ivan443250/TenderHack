from __future__ import annotations

import os

import pytest
from sqlalchemy.ext.asyncio import create_async_engine

from tenderhack_knowledge.contracts.v0 import Corpus, RetrieveRequest
from tenderhack_knowledge.ingestion.repository import UnknownSnapshotError
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository
from tenderhack_knowledge.retrieval.review_k2c import ADVERSARIAL_CASES, evaluate_adversarial
from tenderhack_knowledge.retrieval.service import LexicalRetriever
from tenderhack_knowledge.understanding import understand_query


def test_adversarial_inventory_covers_requested_edges() -> None:
    names = {case.name for case in ADVERSARIAL_CASES}
    assert len(ADVERSARIAL_CASES) == 17
    assert {
        "misspelled_russian",
        "exact_code",
        "leading_zero",
        "mixed_latin_cyrillic_lookalike",
        "short_query",
        "long_description",
        "negation",
        "already_tried",
        "exact_status",
        "same_term_supplier_customer",
        "same_term_different_workflows",
        "table",
        "condition_sensitive_duration",
        "multiple_concepts",
        "irrelevant_contract_number",
        "no_confident_source",
        "sql_injection_payload",
    } == names


def test_understanding_preserves_codes_negation_and_mixed_script() -> None:
    result = understand_query("\u043d\u0435 \u043e\u0442\u043f\u0440\u0430\u0432\u043b\u044f\u0442\u044c \u0423\u041f\u0414-00017 \u0431\u0435\u0437 \u041c\u0427\u0414")
    assert "\u0423\u041f\u0414-00017" in result.exact_codes
    assert "\u041c\u0427\u0414" in result.exact_codes
    assert "\u043d\u0435" in result.normalized_query

    lookalike = understand_query("\u0421SP \u043f\u0440\u043e\u0444\u0438\u043b\u044c")
    assert "CSP" not in lookalike.exact_codes


@pytest.mark.asyncio
async def test_current_snapshot_adversarial_suite() -> None:
    url = os.getenv("K2_TEST_DATABASE_URL", "")
    if not url:
        pytest.skip("K2_TEST_DATABASE_URL is not configured")
    engine = create_async_engine(url, pool_pre_ping=True)
    try:
        repository = PostgresKnowledgeRepository(engine)
        rows, _ = await evaluate_adversarial(repository, repeats=3)
        assert rows
        assert all(row["passed"] for row in rows), rows
        assert all(row["stable"] for row in rows), rows

        # Historical data may never be reached implicitly by a normative
        # retrieval request; an unknown snapshot must fail closed as well.
        retriever = LexicalRetriever(repository)
        with pytest.raises(UnknownSnapshotError):
            await retriever.retrieve(
                RetrieveRequest(query="YML", entities=[], exact_codes=[], corpus=Corpus.HISTORICAL),
                limit=10,
            )
        with pytest.raises(UnknownSnapshotError):
            await retriever.retrieve(
                RetrieveRequest(query="YML", entities=[], exact_codes=[], corpus=Corpus.NORMATIVE, snapshot_id="snap-k2c-unknown"),
                limit=10,
            )
    finally:
        await engine.dispose()
