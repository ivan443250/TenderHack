"""K2A exact + PostgreSQL Full Text Search retrieval baseline."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal

from tenderhack_knowledge.contracts.v0 import Candidate, CandidateScores, Corpus, RetrieveRequest
from tenderhack_knowledge.ingestion.ids import normalize_source_name
from tenderhack_knowledge.ingestion.repository import CorpusBoundaryError, UnknownSnapshotError
from tenderhack_knowledge.historical_support.importer import HISTORICAL_SOURCE_TITLE
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository, TRIGRAM_MIN_SCORE
from tenderhack_knowledge.understanding import normalize_query


class RetrievalError(RuntimeError):
    """Base error for lexical retrieval failures."""


@dataclass(frozen=True)
class RetrievalRecord:
    """Internal result retaining which lexical channels contributed a candidate."""

    candidate: Candidate
    channels: tuple[Literal["exact", "fts", "trigram"], ...]
    text: str = ""


@dataclass(frozen=True)
class RetrievalResult:
    snapshot_id: str
    candidates: tuple[Candidate, ...]
    records: tuple[RetrievalRecord, ...] = ()


class LexicalRetriever:
    """Resolve a snapshot and rank its fragments with exact, FTS and trigram signals."""

    config_version = "lexical-v1"

    def __init__(self, repository: PostgresKnowledgeRepository) -> None:
        self.repository = repository

    async def retrieve(
        self,
        payload: RetrieveRequest,
        *,
        limit: int = 30,
        allow_trigram: bool = True,
        mode: Literal["exact", "fts", "hybrid"] = "hybrid",
    ) -> RetrievalResult:
        if mode not in {"exact", "fts", "hybrid"}:
            raise ValueError(f"unsupported retrieval mode: {mode}")
        snapshot = await self._resolve_snapshot(payload)
        search_query = normalize_query(payload.query)
        exact_codes = payload.exact_codes if mode != "fts" else []
        # A request containing an explicit code must never be typo-mutated by
        # the fuzzy channel.  FTS and exact matching remain available.
        trigram_enabled = allow_trigram and mode == "hybrid" and not exact_codes
        rows = await self.repository.lexical_search(
            snapshot.snapshot_id,
            search_query,
            exact_codes,
            limit=limit,
            allow_trigram=trigram_enabled,
            use_fts=mode != "exact",
        )
        candidates: list[Candidate] = []
        records: list[RetrievalRecord] = []
        historical = snapshot.corpus.value == Corpus.HISTORICAL.value
        for row in rows:
            exact = float(row["exact_score"] or 0.0)
            fts = float(row["fts_score"] or 0.0)
            trigram = float(row["trigram_score"] or 0.0)
            anchor = _anchor_text(row["source_anchor"])
            candidates.append(
                Candidate(
                    fragment_id=row["fragment_id"],
                    document_id="historical-support" if historical else row["document_id"],
                    page=None if historical else row["page_start"],
                    anchor=None if historical else anchor,
                    title=HISTORICAL_SOURCE_TITLE if historical else _source_title(row.get("original_filename")),
                    scores=CandidateScores(
                        exact=exact if exact > 0 else None,
                        fts=fts if fts > 0 else None,
                        dense=None,
                        rerank=None,
                    ),
                    applicability_flags=["HISTORICAL_SUPPORT_SOLUTION"] if historical else [],
                )
            )
            channels: list[Literal["exact", "fts", "trigram"]] = []
            if exact > 0:
                channels.append("exact")
            if fts > 0:
                channels.append("fts")
            if trigram >= TRIGRAM_MIN_SCORE and trigram_enabled:
                channels.append("trigram")
            records.append(
                RetrievalRecord(candidate=candidates[-1], channels=tuple(channels), text=str(row["text"] or ""))
            )
        return RetrievalResult(snapshot_id=snapshot.snapshot_id, candidates=tuple(candidates), records=tuple(records))

    async def _resolve_snapshot(self, payload: RetrieveRequest):
        if payload.snapshot_id:
            snapshot = await self.repository.get_snapshot(payload.snapshot_id)
            if snapshot is None:
                raise UnknownSnapshotError(payload.snapshot_id)
        else:
            snapshot = await self.repository.get_current_snapshot(payload.corpus.value)
            if snapshot is None:
                raise UnknownSnapshotError(f"no current {payload.corpus.value.casefold()} snapshot")
        if snapshot.corpus.value != payload.corpus.value:
            raise CorpusBoundaryError(
                f"snapshot corpus {snapshot.corpus.value} does not match requested corpus {payload.corpus.value}"
            )
        return snapshot


def _anchor_text(anchor: object) -> str | None:
    if not isinstance(anchor, dict):
        return None
    bbox = anchor.get("bbox")
    if isinstance(bbox, (list, tuple)) and len(bbox) == 4:
        return "bbox:" + ",".join(str(value) for value in bbox)
    section = anchor.get("section")
    return str(section) if section else None


def _source_title(filename: object) -> str | None:
    """Return the normalized basename, never a local source path."""

    if not isinstance(filename, str) or not filename.strip():
        return None
    normalized = normalize_source_name(filename)
    return normalized.rsplit("/", 1)[-1] or None
