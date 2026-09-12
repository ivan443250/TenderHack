from __future__ import annotations

from dataclasses import dataclass

import pytest

from tenderhack_knowledge.contracts.v0 import Candidate, CandidateScores, Corpus, RetrieveRequest
from tenderhack_knowledge.inference.model_refs import (
    EMBEDDING_MODEL_ID,
    EMBEDDING_REVISION,
    RERANKER_MODEL_ID,
    RERANKER_REVISION,
)
from tenderhack_knowledge.retrieval.hybrid import HybridRetriever
from tenderhack_knowledge.retrieval.service import RetrievalRecord, RetrievalResult


def _request() -> RetrieveRequest:
    return RetrieveRequest(query="как отправить УПД", entities=[], exact_codes=["УПД"], corpus=Corpus.NORMATIVE, snapshot_id="snap")


def _candidate(fragment_id: str, *, exact: float | None = None, fts: float | None = None) -> Candidate:
    return Candidate(
        fragment_id=fragment_id,
        document_id="doc",
        page=1,
        anchor=None,
        scores=CandidateScores(exact=exact, fts=fts),
        applicability_flags=[],
    )


class LexicalStub:
    def __init__(self, exact: tuple[RetrievalRecord, ...], fts: tuple[RetrievalRecord, ...]) -> None:
        self.exact = exact
        self.fts = fts

    async def retrieve(self, _payload: RetrieveRequest, *, mode: str, **_: object) -> RetrievalResult:
        records = self.exact if mode == "exact" else self.fts
        return RetrievalResult(snapshot_id="snap", candidates=tuple(record.candidate for record in records), records=records)


class RepositoryStub:
    async def dense_search(self, *_args: object, limit: int, **_kwargs: object) -> tuple[dict[str, object], ...]:
        return (
            {"fragment_id": "f2", "document_id": "doc", "page_start": 2, "source_anchor": {}, "text": "second", "dense_score": 0.9},
            {"fragment_id": "f3", "document_id": "doc", "page_start": 3, "source_anchor": {}, "text": "third", "dense_score": 0.8},
        )[:limit]


class EmbedderStub:
    model_id = EMBEDDING_MODEL_ID
    revision = EMBEDDING_REVISION
    dimension = 1024

    async def embed_query(self, _query: str) -> list[float]:
        return [0.0] * 1024


class RerankerStub:
    model_id = RERANKER_MODEL_ID
    revision = RERANKER_REVISION

    async def score(self, _query: str, candidates: list[str]) -> list[float]:
        return [float(len(candidates) - index) for index, _ in enumerate(candidates)]


def _retriever(*, reranker: object | None = None, embedder: object | None = None) -> HybridRetriever:
    repository = RepositoryStub()
    retriever = HybridRetriever(repository, embedder=embedder or EmbedderStub(), reranker=reranker or RerankerStub())
    retriever.lexical = LexicalStub(
        (
            RetrievalRecord(_candidate("f1", exact=1.0), ("exact",), "first"),
            RetrievalRecord(_candidate("f2", exact=1.0), ("exact",), "second"),
        ),
        (
            RetrievalRecord(_candidate("f2", fts=0.7), ("fts",), "second"),
            RetrievalRecord(_candidate("f3", fts=0.6), ("fts",), "third"),
        ),
    )
    return retriever


@pytest.mark.asyncio
async def test_rrf_is_deterministic_and_deduplicates_fragment_ids() -> None:
    first = await _retriever().retrieve(_request())
    second = await _retriever().retrieve(_request())
    assert [candidate.fragment_id for candidate in first.candidates] == [candidate.fragment_id for candidate in second.candidates]
    assert len({candidate.fragment_id for candidate in first.candidates}) == len(first.candidates)
    assert first.config_version == "hybrid-rrf-v1"
    assert first.candidates[0].scores.rerank is not None
    assert first.candidates[0].scores.dense is not None


@pytest.mark.asyncio
async def test_embedding_dimension_and_revision_are_checked_before_vector_query() -> None:
    class WrongDimension(EmbedderStub):
        dimension = 768

    wrong_dimension = _retriever(embedder=WrongDimension())
    wrong_dimension.allow_lexical_fallback = False
    with pytest.raises(Exception, match="1024"):
        await wrong_dimension.retrieve(_request())

    class WrongRevision(EmbedderStub):
        revision = "different"

    with pytest.raises(Exception, match="revision"):
        wrong_revision = _retriever(embedder=WrongRevision())
        wrong_revision.allow_lexical_fallback = False
        await wrong_revision.retrieve(_request())


@pytest.mark.asyncio
async def test_reranker_failure_is_versioned_and_preserves_fused_cardinality() -> None:
    class FailingReranker(RerankerStub):
        async def score(self, _query: str, _candidates: list[str]) -> list[float]:
            raise RuntimeError("offline")

    result = await _retriever(reranker=FailingReranker()).retrieve(_request(), final_limit=10)
    assert result.config_version == "hybrid-rrf-v1-no-reranker"
    assert result.degraded_reason and "offline" in result.degraded_reason
    assert len(result.candidates) == 3
    assert len({candidate.fragment_id for candidate in result.candidates}) == 3

    class WrongCardinality(RerankerStub):
        async def score(self, _query: str, _candidates: list[str]) -> list[float]:
            return [1.0]

    malformed = await _retriever(reranker=WrongCardinality()).retrieve(_request(), final_limit=10)
    assert malformed.config_version == "hybrid-rrf-v1-no-reranker"
    assert len(malformed.candidates) == 3


@pytest.mark.asyncio
async def test_dense_unavailable_uses_explicit_no_dense_fallback() -> None:
    class FailingEmbedder(EmbedderStub):
        async def embed_query(self, _query: str) -> list[float]:
            raise RuntimeError("model unavailable")

    result = await _retriever(embedder=FailingEmbedder()).retrieve(_request())
    assert result.config_version == "hybrid-rrf-v1-no-dense"
    assert result.degraded_reason and "dense_unavailable" in result.degraded_reason
    assert all(candidate.scores.dense is None for candidate in result.candidates)
