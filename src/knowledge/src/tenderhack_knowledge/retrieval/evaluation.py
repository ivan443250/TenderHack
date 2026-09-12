"""Offline runner for the K2A manually grounded lexical retrieval gold set."""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import statistics
import time
from pathlib import Path
from typing import Any

from sqlalchemy.ext.asyncio import create_async_engine

from tenderhack_knowledge.contracts.v0 import Corpus, RetrieveRequest
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository
from tenderhack_knowledge.retrieval.service import LexicalRetriever


def _percentile(values: list[float], percentile: float) -> float | None:
    if not values:
        return None
    values = sorted(values)
    index = min(len(values) - 1, max(0, round((percentile / 100) * (len(values) - 1))))
    return round(values[index], 3)


def _query_metrics(rows: list[dict[str, Any]], *, k: int) -> dict[str, float | int | None]:
    if not rows:
        return {f"recall_at_{k}": None, "mrr": None, "query_count": 0}
    hits = []
    reciprocal_ranks = []
    for row in rows:
        expected = set(row["expected_fragment_ids"])
        found = [candidate.fragment_id for candidate in row["candidates"][:k]]
        rank = next((index + 1 for index, fragment_id in enumerate(found) if fragment_id in expected), None)
        hits.append(rank is not None)
        reciprocal_ranks.append(1 / rank if rank is not None else 0.0)
    return {
        f"recall_at_{k}": round(sum(hits) / len(hits), 4),
        "mrr": round(sum(reciprocal_ranks) / len(reciprocal_ranks), 4),
        "query_count": len(rows),
    }


async def evaluate_gold(gold_path: Path, database_url: str, snapshot_id: str) -> dict[str, Any]:
    gold = json.loads(gold_path.read_text(encoding="utf-8"))
    queries = gold["queries"]
    engine = create_async_engine(database_url, pool_pre_ping=True)
    try:
        retriever = LexicalRetriever(PostgresKnowledgeRepository(engine))
        level_rows: dict[str, list[dict[str, Any]]] = {"L0_exact": [], "L1_fts": [], "L2_hybrid_trigram": []}
        latencies: list[float] = []
        for item in queries:
            request = RetrieveRequest(
                query=item["query"],
                entities=[],
                exact_codes=item.get("exact_codes", []),
                corpus=Corpus.NORMATIVE,
                snapshot_id=snapshot_id,
            )
            for level, mode in (("L0_exact", "exact"), ("L1_fts", "fts"), ("L2_hybrid_trigram", "hybrid")):
                started = time.perf_counter()
                result = await retriever.retrieve(request, mode=mode, allow_trigram=True, limit=30)
                elapsed_ms = (time.perf_counter() - started) * 1000
                if level == "L2_hybrid_trigram":
                    latencies.append(elapsed_ms)
                level_rows[level].append({
                    "query_id": item["query_id"],
                    "document": item["document"],
                    "expected_fragment_ids": item["expected_fragment_ids"],
                    "subsets": item.get("subsets", []),
                    "exact_codes": item.get("exact_codes", []),
                    "candidates": list(result.candidates),
                })

        levels: dict[str, Any] = {}
        for level, rows in level_rows.items():
            metrics = _query_metrics(rows, k=5)
            metrics.update({"recall_at_10": _query_metrics(rows, k=10)["recall_at_10"]})
            exact_rows = [row for row in rows if row["exact_codes"]]
            if exact_rows:
                metrics["exact_code_hit_at_10"] = _query_metrics(exact_rows, k=10)["recall_at_10"]
            typo_rows = [row for row in rows if "typo" in row["subsets"]]
            if typo_rows:
                metrics["typo_subset_recall_at_10"] = _query_metrics(typo_rows, k=10)["recall_at_10"]
            levels[level] = metrics

        failures: dict[str, list[str]] = {}
        for row in level_rows["L2_hybrid_trigram"]:
            expected = set(row["expected_fragment_ids"])
            if not any(candidate.fragment_id in expected for candidate in row["candidates"][:10]):
                failures.setdefault(row["document"], []).append(row["query_id"])
        return {
            "benchmark": "k2a-lexical-retrieval",
            "snapshot_id": snapshot_id,
            "gold_set": {"path": str(gold_path), "query_count": len(queries), "label_source": gold["label_source"]},
            "levels": levels,
            "per_document_failures_l2_at_10": failures,
            "latency_ms_l2_hybrid": {
                "p50": _percentile(latencies, 50),
                "p95": _percentile(latencies, 95),
                "sample_count": len(latencies),
                "mean": round(statistics.mean(latencies), 3) if latencies else None,
            },
            "notes": [
                "Metrics are measured against the running PostgreSQL corpus; they describe retrieval only, not answer correctness.",
                "L0 exact uses extracted exact_codes only; L1 uses PostgreSQL FTS; L2 adds the weak pg_trgm pass for non-code queries.",
            ],
        }
    finally:
        await engine.dispose()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gold", type=Path, required=True)
    parser.add_argument("--database-url", default=os.getenv("DATABASE_URL", ""))
    parser.add_argument("--snapshot-id", required=True)
    args = parser.parse_args()
    if not args.database_url:
        raise SystemExit("--database-url or DATABASE_URL is required")
    print(json.dumps(asyncio.run(evaluate_gold(args.gold, args.database_url, args.snapshot_id)), ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
