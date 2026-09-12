"""Snapshot-constrained dense + lexical RRF retrieval with optional reranking.

The implementation deliberately keeps fusion and failure semantics in the
knowledge runtime.  It returns evidence candidates only; business decisions
remain owned by the .NET support core.
"""

from __future__ import annotations

import math
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from typing import Any

from tenderhack_knowledge.contracts.v0 import Candidate, CandidateScores, RetrieveRequest
from tenderhack_knowledge.inference.embedding import Qwen3EmbeddingAdapter
from tenderhack_knowledge.inference.errors import (
    AdapterOutputError,
    EmbeddingRevisionMismatchError,
    EmbeddingUnavailableError,
    RerankerUnavailableError,
)
from tenderhack_knowledge.inference.model_refs import (
    EMBEDDING_MODEL_ID,
    EMBEDDING_REVISION,
    RERANKER_MODEL_ID,
    RERANKER_REVISION,
)
from tenderhack_knowledge.inference.reranker import BgeRerankerAdapter
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository

from .service import LexicalRetriever


RRF_K = 60
DEFAULT_CHANNEL_LIMIT = 30
DEFAULT_RERANK_LIMIT = 20
DEFAULT_FINAL_LIMIT = 10


@dataclass(frozen=True)
class HybridRecord:
    candidate: Candidate
    text: str
    channels: tuple[str, ...]
    rrf_score: float


@dataclass(frozen=True)
class HybridRetrievalResult:
    snapshot_id: str
    candidates: tuple[Candidate, ...]
    records: tuple[HybridRecord, ...]
    config_version: str
    degraded_reason: str | None = None


class HybridRetriever:
    """Fuse exact, PostgreSQL FTS and pgvector results deterministically."""

    config_version = "hybrid-rrf-v1"
    no_dense_config_version = "hybrid-rrf-v1-no-dense"
    rrf_only_config_version = "hybrid-rrf-v1-rrf-only"
    no_reranker_config_version = "hybrid-rrf-v1-no-reranker"

    def __init__(
        self,
        repository: PostgresKnowledgeRepository,
        *,
        embedder: object | None = None,
        reranker: object | None = None,
        rrf_k: int = RRF_K,
        allow_lexical_fallback: bool = True,
        max_embedding_retries: int = 1,
    ) -> None:
        if rrf_k < 1:
            raise ValueError("rrf_k must be positive")
        if max_embedding_retries < 0 or max_embedding_retries > 1:
            raise ValueError("embedding retries are limited to zero or one")
        self.repository = repository
        self.lexical = LexicalRetriever(repository)
        self.embedder = embedder
        self.reranker = reranker
        self.rrf_k = rrf_k
        self.allow_lexical_fallback = allow_lexical_fallback
        self.max_embedding_retries = max_embedding_retries

    async def retrieve(
        self,
        payload: RetrieveRequest,
        *,
        channel_limit: int = DEFAULT_CHANNEL_LIMIT,
        rerank_limit: int = DEFAULT_RERANK_LIMIT,
        final_limit: int = DEFAULT_FINAL_LIMIT,
        rerank: bool = True,
    ) -> HybridRetrievalResult:
        if channel_limit < 1 or rerank_limit < 1 or final_limit < 1:
            raise ValueError("retrieval limits must be positive")
        final_limit = min(int(final_limit), 10)

        # Exact and FTS are separate ranked channels.  Trigram is intentionally
        # not part of K2B fusion; it remains the K2A lexical baseline.
        exact = await self.lexical.retrieve(
            payload, limit=channel_limit, allow_trigram=False, mode="exact"
        )
        fts = await self.lexical.retrieve(
            payload, limit=channel_limit, allow_trigram=False, mode="fts"
        )
        snapshot_id = exact.snapshot_id
        try:
            dense_rows = await self._dense_rows(payload, snapshot_id, channel_limit)
        except (EmbeddingUnavailableError, EmbeddingRevisionMismatchError, AdapterOutputError) as exc:
            if not self.allow_lexical_fallback:
                raise
            fused = self._fuse(
                snapshot_id,
                (self._records(exact), self._records(fts)),
                final_limit=final_limit,
            )
            return HybridRetrievalResult(
                snapshot_id=snapshot_id,
                candidates=tuple(record.candidate for record in fused.records),
                records=fused.records,
                config_version=self.no_dense_config_version,
                degraded_reason=f"dense_unavailable: {type(exc).__name__}: {exc}",
            )

        dense_records = tuple(self._dense_record(row) for row in dense_rows)
        fused = self._fuse(
            snapshot_id,
            (self._records(exact), self._records(fts), dense_records),
            final_limit=max(final_limit, min(rerank_limit, DEFAULT_RERANK_LIMIT)),
        )

        if not rerank:
            selected = tuple(fused.records[:final_limit])
            return HybridRetrievalResult(
                snapshot_id=snapshot_id,
                candidates=tuple(record.candidate for record in selected),
                records=selected,
                config_version=self.rrf_only_config_version,
            )

        rerank_count = min(int(rerank_limit), len(fused.records), DEFAULT_RERANK_LIMIT)
        if rerank_count == 0:
            return HybridRetrievalResult(
                snapshot_id=snapshot_id,
                candidates=(),
                records=(),
                config_version=self.config_version,
            )
        try:
            reranked = await self._rerank(payload.query, fused.records[:rerank_count])
            selected = tuple(reranked[:final_limit])
            return HybridRetrievalResult(
                snapshot_id=snapshot_id,
                candidates=tuple(record.candidate for record in selected),
                records=selected,
                config_version=self.config_version,
            )
        except (RerankerUnavailableError, AdapterOutputError) as exc:
            # A missing optional reranker never erases valid fused evidence, but
            # its absence is visible in the version and diagnostic reason.
            selected = tuple(fused.records[:final_limit])
            return HybridRetrievalResult(
                snapshot_id=snapshot_id,
                candidates=tuple(record.candidate for record in selected),
                records=selected,
                config_version=self.no_reranker_config_version,
                degraded_reason=f"reranker_unavailable: {type(exc).__name__}: {exc}",
            )

    async def _dense_rows(
        self, payload: RetrieveRequest, snapshot_id: str, limit: int
    ) -> tuple[dict[str, object], ...]:
        embedder = self.embedder
        if embedder is None:
            # Request handling must never trigger a network download.  The
            # explicit backfill/smoke commands are the only download edges.
            embedder = Qwen3EmbeddingAdapter(local_files_only=True)
            self.embedder = embedder
        _check_embedding_metadata(embedder)
        method = getattr(embedder, "embed_query", None)
        if not callable(method):
            method = getattr(embedder, "embed", None)
        if not callable(method):
            raise EmbeddingUnavailableError("embedding adapter does not expose embed_query")
        last_error: Exception | None = None
        for _attempt in range(self.max_embedding_retries + 1):
            try:
                value = await method(payload.query)
                vector = value[0] if _looks_like_matrix(value) else value
                _validate_query_vector(vector)
            except (EmbeddingRevisionMismatchError, AdapterOutputError):
                raise
            except Exception as exc:  # optional runtime/transport failures
                last_error = exc
                continue
            return await self.repository.dense_search(
                snapshot_id,
                vector,
                model_id=EMBEDDING_MODEL_ID,
                model_revision=EMBEDDING_REVISION,
                dimension=1024,
                limit=limit,
            )
        raise EmbeddingUnavailableError(str(last_error or "embedding failed")) from last_error

    async def _rerank(self, query: str, records: Sequence[HybridRecord]) -> tuple[HybridRecord, ...]:
        reranker = self.reranker
        if reranker is None:
            reranker = BgeRerankerAdapter(local_files_only=True)
            self.reranker = reranker
        if getattr(reranker, "model_id", RERANKER_MODEL_ID) != RERANKER_MODEL_ID:
            raise RerankerUnavailableError("unexpected reranker model_id")
        if getattr(reranker, "revision", RERANKER_REVISION) != RERANKER_REVISION:
            raise RerankerUnavailableError("unexpected reranker revision")
        score_method = getattr(reranker, "score", None)
        if not callable(score_method):
            raise RerankerUnavailableError("reranker adapter does not expose score")
        try:
            scores = list(await score_method(query, [record.text for record in records]))
        except Exception as exc:
            raise RerankerUnavailableError(f"reranker scoring failed: {exc}") from exc
        if len(scores) != len(records):
            raise AdapterOutputError(
                f"reranker returned {len(scores)} scores for {len(records)} candidates"
            )
        updated: list[HybridRecord] = []
        for record, score in zip(records, scores, strict=True):
            value = float(score)
            if not math.isfinite(value):
                raise AdapterOutputError("reranker returned a non-finite score")
            updated.append(
                HybridRecord(
                    candidate=record.candidate.model_copy(
                        update={
                            "scores": record.candidate.scores.model_copy(update={"rerank": value})
                        }
                    ),
                    text=record.text,
                    channels=record.channels,
                    rrf_score=record.rrf_score,
                )
            )
        updated.sort(key=lambda item: (-float(item.candidate.scores.rerank or 0.0), -item.rrf_score, item.candidate.fragment_id))
        return tuple(updated)

    def _fuse(
        self,
        snapshot_id: str,
        channels: Sequence[Sequence[HybridRecord]],
        *,
        final_limit: int,
    ) -> HybridRetrievalResult:
        merged: dict[str, dict[str, Any]] = {}
        for channel in channels:
            seen: set[str] = set()
            for rank, record in enumerate(channel, start=1):
                fragment_id = record.candidate.fragment_id
                if fragment_id in seen:
                    continue
                seen.add(fragment_id)
                item = merged.setdefault(
                    fragment_id,
                    {
                        "candidate": record.candidate,
                        "text": record.text,
                        "channels": set(),
                        "rrf_score": 0.0,
                    },
                )
                item["rrf_score"] += 1.0 / (self.rrf_k + rank)
                item["channels"].update(record.channels)
                if not item["text"]:
                    item["text"] = record.text
                item["candidate"] = _merge_scores(item["candidate"], record.candidate)
        records = [
            HybridRecord(
                candidate=item["candidate"],
                text=item["text"],
                channels=tuple(sorted(item["channels"])),
                rrf_score=float(item["rrf_score"]),
            )
            for item in merged.values()
        ]
        records.sort(key=lambda item: (-item.rrf_score, item.candidate.fragment_id))
        selected = tuple(records[: max(1, int(final_limit))])
        return HybridRetrievalResult(
            snapshot_id=snapshot_id,
            candidates=tuple(record.candidate for record in selected),
            records=selected,
            config_version=self.config_version,
        )

    @staticmethod
    def _records(result: Any) -> tuple[HybridRecord, ...]:
        return tuple(
            HybridRecord(
                candidate=record.candidate,
                text=record.text,
                channels=record.channels,
                rrf_score=0.0,
            )
            for record in result.records
        )

    @staticmethod
    def _dense_record(row: Mapping[str, object]) -> HybridRecord:
        score = float(row["dense_score"] or 0.0)
        if not math.isfinite(score):
            raise AdapterOutputError("dense search returned a non-finite score")
        anchor = row.get("source_anchor")
        anchor_text = None
        if isinstance(anchor, dict):
            bbox = anchor.get("bbox")
            if isinstance(bbox, (list, tuple)) and len(bbox) == 4:
                anchor_text = "bbox:" + ",".join(str(value) for value in bbox)
            elif anchor.get("section"):
                anchor_text = str(anchor["section"])
        candidate = Candidate(
            fragment_id=str(row["fragment_id"]),
            document_id=str(row["document_id"]),
            page=int(row["page_start"]),
            anchor=anchor_text,
            scores=CandidateScores(dense=score),
            applicability_flags=[],
        )
        return HybridRecord(candidate=candidate, text=str(row.get("text") or ""), channels=("dense",), rrf_score=0.0)

def _merge_scores(left: Candidate, right: Candidate) -> Candidate:
    values = {
        "exact": left.scores.exact if left.scores.exact is not None else right.scores.exact,
        "fts": left.scores.fts if left.scores.fts is not None else right.scores.fts,
        "dense": left.scores.dense if left.scores.dense is not None else right.scores.dense,
        "rerank": left.scores.rerank if left.scores.rerank is not None else right.scores.rerank,
    }
    return left.model_copy(update={"scores": CandidateScores(**values)})


def _check_embedding_metadata(adapter: object) -> None:
    model_id = getattr(adapter, "model_id", EMBEDDING_MODEL_ID)
    revision = getattr(adapter, "revision", EMBEDDING_REVISION)
    dimension = getattr(adapter, "dimension", 1024)
    if model_id != EMBEDDING_MODEL_ID or revision != EMBEDDING_REVISION:
        raise EmbeddingRevisionMismatchError(
            f"embedding model_revision metadata {(model_id, revision)} does not match "
            f"{(EMBEDDING_MODEL_ID, EMBEDDING_REVISION)}"
        )
    if int(dimension) != 1024:
        raise AdapterOutputError(f"embedding dimension {dimension}; expected 1024")


def _validate_query_vector(vector: object) -> None:
    if not isinstance(vector, (list, tuple)) or len(vector) != 1024:
        raise AdapterOutputError("query embedding must contain exactly 1024 values")
    try:
        if any(not math.isfinite(float(value)) for value in vector):
            raise AdapterOutputError("query embedding contains a non-finite value")
    except (TypeError, ValueError) as exc:
        raise AdapterOutputError("query embedding contains a non-numeric value") from exc


def _looks_like_matrix(value: object) -> bool:
    return isinstance(value, (list, tuple)) and bool(value) and isinstance(value[0], (list, tuple))
