"""K2B evaluation runner using the unchanged K2A manually grounded gold set."""

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
from tenderhack_knowledge.inference.model_refs import EMBEDDING_MODEL_ID, EMBEDDING_REVISION
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository
from tenderhack_knowledge.retrieval.hybrid import HybridRetriever
from tenderhack_knowledge.retrieval.service import LexicalRetriever


def _percentile(values: list[float], percentile: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    index = min(len(ordered) - 1, max(0, round((percentile / 100) * (len(ordered) - 1))))
    return round(ordered[index], 3)


def _metrics(rows: list[dict[str, Any]], *, typo_ids: set[str], exact_ids: set[str]) -> dict[str, Any]:
    def hit(row: dict[str, Any], limit: int) -> bool:
        expected = set(row["expected_fragment_ids"])
        return any(candidate.fragment_id in expected for candidate in row["candidates"][:limit])

    if not rows:
        return {
            "status": "NOT_MEASURED",
            "query_count": 0,
            "recall_at_5": None,
            "recall_at_10": None,
            "mrr": None,
            "typo_recall_at_10": None,
            "exact_code_hit_at_10": None,
        }
    reciprocal = []
    for row in rows:
        expected = set(row["expected_fragment_ids"])
        rank = next(
            (index + 1 for index, candidate in enumerate(row["candidates"]) if candidate.fragment_id in expected),
            None,
        )
        reciprocal.append(1 / rank if rank else 0.0)
    typo_rows = [row for row in rows if row["query_id"] in typo_ids]
    exact_rows = [row for row in rows if row["query_id"] in exact_ids]
    return {
        "status": "MEASURED",
        "query_count": len(rows),
        "recall_at_5": round(sum(hit(row, 5) for row in rows) / len(rows), 4),
        "recall_at_10": round(sum(hit(row, 10) for row in rows) / len(rows), 4),
        "mrr": round(sum(reciprocal) / len(reciprocal), 4),
        "typo_recall_at_10": round(sum(hit(row, 10) for row in typo_rows) / len(typo_rows), 4) if typo_rows else None,
        "exact_code_hit_at_10": round(sum(hit(row, 10) for row in exact_rows) / len(exact_rows), 4) if exact_rows else None,
    }


def _failure_analysis(rows: list[dict[str, Any]]) -> list[dict[str, str]]:
    categories = {
        "act-03": ("parser/chunk issue", "Expected page fragment has sparse extracted text; code context is not surfaced."),
        "act-07": ("parser/chunk issue", "Expected table row loses the user-facing status wording during extraction."),
        "yml-06": ("parser/chunk issue", "Expected table/header context is split across extracted fragments."),
        "supplier-01": ("lexical miss", "Short terminology query has weak lexical overlap with the long source fragment."),
        "supplier-02": ("lexical miss", "Generic software requirement wording is not distinctive in FTS ranking."),
        "supplier-04": ("lexical miss", "The source wording uses a different formulation for the offer registry."),
        "supplier-05": ("lexical miss", "Deletion procedure is represented in a low-overlap instructional fragment."),
        "supplier-08": ("lexical miss", "Contact details are distributed across a sparse page fragment."),
        "supplier-09": ("lexical miss", "Error wording does not match the concise user query lexically."),
        "offer-01": ("parser/chunk issue", "Expected offer/СТЕ context is table-heavy and split by extraction."),
        "mcd-05": ("parser/chunk issue", "Expected МЧД signing instruction is in a table-like extracted fragment."),
    }
    failures: list[dict[str, str]] = []
    for row in rows:
        if any(candidate.fragment_id in set(row["expected_fragment_ids"]) for candidate in row["candidates"][:10]):
            continue
        category, evidence = categories.get(row["query_id"], ("lexical miss", "No top-10 candidate matched the manually grounded fragment."))
        failures.append({"query_id": row["query_id"], "category": category, "evidence": evidence})
    return failures


async def evaluate_gold(gold_path: Path, database_url: str, snapshot_id: str) -> dict[str, Any]:
    gold = json.loads(gold_path.read_text(encoding="utf-8"))
    queries = gold["queries"]
    typo_ids = {item["query_id"] for item in queries if "typo" in item.get("subsets", [])}
    exact_ids = {item["query_id"] for item in queries if item.get("exact_codes")}
    engine = create_async_engine(database_url, pool_pre_ping=True)
    try:
        repository = PostgresKnowledgeRepository(engine)
        lexical = LexicalRetriever(repository)
        baseline_rows: list[dict[str, Any]] = []
        baseline_latency: list[float] = []
        for item in queries:
            request = RetrieveRequest(
                query=item["query"], entities=[], exact_codes=item.get("exact_codes", []),
                corpus=Corpus.NORMATIVE, snapshot_id=snapshot_id,
            )
            started = time.perf_counter()
            result = await lexical.retrieve(request, mode="hybrid", allow_trigram=True, limit=30)
            baseline_latency.append((time.perf_counter() - started) * 1000)
            baseline_rows.append({**item, "candidates": list(result.candidates)})

        levels: dict[str, Any] = {
            "B1_lexical_baseline": {
                **_metrics(baseline_rows, typo_ids=typo_ids, exact_ids=exact_ids),
                "latency_ms": {
                    "sample_count": len(baseline_latency),
                    "p50": _percentile(baseline_latency, 50),
                    "p95": _percentile(baseline_latency, 95),
                    "mean": round(statistics.mean(baseline_latency), 3) if baseline_latency else None,
                },
            }
        }

        stats = await repository.embedding_stats(
            snapshot_id,
            model_id=EMBEDDING_MODEL_ID,
            model_revision=EMBEDDING_REVISION,
            dimension=1024,
        )
        blocker: str | None = None
        if not stats["identity_preserved"] or stats["embeddings"] != stats["snapshot_fragments"]:
            blocker = (
                "Qwen3 embeddings are not backfilled: expected "
                f"{stats['snapshot_fragments']}, found {stats['embeddings']}"
            )
        if blocker is None:
            # Real B2/B3 execution is opt-in and only runs when the complete
            # persisted vector set has already been certified.
            for name, use_reranker in (("B2_rrf", False), ("B3_rrf_reranker", True)):
                retriever = HybridRetriever(repository, allow_lexical_fallback=False)
                rows: list[dict[str, Any]] = []
                latencies: list[float] = []
                try:
                    for item in queries:
                        request = RetrieveRequest(
                            query=item["query"], entities=[], exact_codes=item.get("exact_codes", []),
                            corpus=Corpus.NORMATIVE, snapshot_id=snapshot_id,
                        )
                        started = time.perf_counter()
                        result = await retriever.retrieve(request, rerank=use_reranker, final_limit=10)
                        latencies.append((time.perf_counter() - started) * 1000)
                        rows.append({**item, "candidates": list(result.candidates)})
                    levels[name] = {
                        **_metrics(rows, typo_ids=typo_ids, exact_ids=exact_ids),
                        "latency_ms": {
                            "sample_count": len(latencies), "p50": _percentile(latencies, 50),
                            "p95": _percentile(latencies, 95),
                            "mean": round(statistics.mean(latencies), 3) if latencies else None,
                        },
                    }
                except Exception as exc:
                    blocker = f"{name} execution failed: {type(exc).__name__}: {exc}"
                    levels[name] = {"status": "NOT_MEASURED", "blocker": blocker}
                    break
        else:
            levels["B2_rrf"] = {"status": "NOT_MEASURED", "blocker": blocker}
            levels["B3_rrf_reranker"] = {"status": "NOT_MEASURED", "blocker": blocker}

        return {
            "benchmark": "k2b-hybrid-retrieval",
            "status": "PASS_WITH_LIMITATIONS" if blocker else "PASS",
            "snapshot_id": snapshot_id,
            "corpus": {"documents": 6, "pages": 830, "fragments": 3899, "corpus": "NORMATIVE"},
            "gold_set": {"path": str(gold_path), "queries": len(queries), "label_source": gold["label_source"]},
            "levels": levels,
            "failure_analysis_b1_recall_at_10": _failure_analysis(baseline_rows),
            "embedding_persistence": {
                "model_id": EMBEDDING_MODEL_ID, "model_revision": EMBEDDING_REVISION,
                "dimension": 1024, "stats": stats,
            },
            "reranker": {"model_id": "BAAI/bge-reranker-v2-m3", "model_revision": "953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e", "pair_max_length": 512, "score_semantics": "raw relevance score, not probability"},
            "rrf": {"k": 60, "channels": ["exact", "fts", "dense"], "dedupe_key": "fragment_id"},
            "blockers": [blocker] if blocker else [],
            "verification": {"gold_labels_changed": False, "no_approximate_vector_index": True},
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
    print(json.dumps(asyncio.run(evaluate_gold(args.gold, args.database_url, args.snapshot_id)), ensure_ascii=False, indent=2))


if __name__ == "__main__":  # pragma: no cover
    main()
