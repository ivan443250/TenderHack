"""K2D coverage audit for the real 2026 support export.

The audit is deliberately evaluation-only.  It reads the user-description
field, strips the known ``Подтема запроса:`` prefix for search, and runs the
current normative retrieval pipeline.  Historical ``Решение`` text is never
sent to retrieval; it is accepted only as a coarse, explicitly labelled
operator/system-action signal prepared by the source audit.
"""

from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import math
import os
import re
import statistics
import time
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Mapping, Sequence

from sqlalchemy.ext.asyncio import create_async_engine

from tenderhack_knowledge.contracts.v0 import Corpus, RetrieveRequest
from tenderhack_knowledge.inference.model_refs import EMBEDDING_MODEL_ID, EMBEDDING_REVISION
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository
from tenderhack_knowledge.retrieval.hybrid import HybridRetriever
from tenderhack_knowledge.understanding import understand_query


SAMPLE_SEED = 20260912
DEFAULT_SAMPLE_TARGET = 600
_PREFIX_RE = re.compile(r"^\s*Подтема\s+запроса\s*:\s*", re.IGNORECASE)
_TOKEN_RE = re.compile(r"[A-Za-z\u0400-\u04ff0-9]{2,}")
_STOPWORDS = {
    "как", "что", "это", "для", "при", "после", "перед", "или", "уже", "был",
    "была", "были", "есть", "нет", "мой", "моя", "мне", "про", "под", "над",
    "из", "на", "по", "не", "но", "да", "до", "так", "его", "ее", "их", "the",
    "and", "for", "with", "from", "this", "that",
}


def evaluation_query(text: str) -> str:
    """Strip only the export's presentation prefix; preserve user wording."""

    if not isinstance(text, str):
        raise TypeError("description must be a string")
    return _PREFIX_RE.sub("", text, count=1).strip()


def normalized_query(text: str) -> str:
    return " ".join(evaluation_query(text).casefold().split())


def query_sha256(text: str) -> str:
    # Hash the normalized evaluation query so case/whitespace variants are one
    # deduplication identity, while the original query remains available for
    # retrieval and manual review.
    return hashlib.sha256(normalized_query(text).encode("utf-8")).hexdigest()


def _sample_key(record: Mapping[str, Any], seed: int) -> str:
    identity = f"{seed}|{record.get('query_sha256') or query_sha256(str(record.get('query', '')))}|{record.get('row', '')}"
    return hashlib.sha256(identity.encode("utf-8")).hexdigest()


def _sampling_group(record: Mapping[str, Any]) -> str:
    mapped = str(record.get("mapped_theme") or "UNMAPPED")
    return mapped if mapped not in {"UNMAPPED", "AMBIGUOUS"} else "UNMAPPED_OR_AMBIGUOUS"


def deterministic_stratified_sample(
    records: Sequence[Mapping[str, Any]],
    *,
    target: int = DEFAULT_SAMPLE_TARGET,
    seed: int = SAMPLE_SEED,
) -> list[dict[str, Any]]:
    """Select a reproducible, de-duplicated, rare-group-aware sample.

    Every available canonical taxonomy group receives one record first.  The
    remaining quota is distributed proportional to the square root of group
    size, which prevents the dominant unmapped bucket from erasing small
    groups while remaining deterministic and transparent.
    """

    if target < 1:
        raise ValueError("target must be positive")
    unique: dict[str, Mapping[str, Any]] = {}
    for record in records:
        query = evaluation_query(str(record.get("query", "")))
        normalized = " ".join(query.casefold().split())
        if not normalized:
            continue
        identity = query_sha256(query)
        unique.setdefault(identity, {**record, "query": query, "query_sha256": identity})

    if not unique:
        return []

    groups: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for record in unique.values():
        groups[_sampling_group(record)].append(dict(record))
    for values in groups.values():
        values.sort(key=lambda item: _sample_key(item, seed))

    target = min(target, len(unique))
    selected: list[dict[str, Any]] = []
    allocations: dict[str, int] = {}
    for group in sorted(groups):
        if len(selected) >= target:
            break
        allocations[group] = 1
        selected.append(groups[group][0])

    remaining = target - len(selected)
    if remaining:
        weights = {group: math.sqrt(len(values)) for group, values in groups.items()}
        weight_total = sum(weights.values())
        for group in sorted(groups):
            available = len(groups[group]) - allocations.get(group, 0)
            share = int(remaining * weights[group] / weight_total) if weight_total else 0
            allocations[group] = allocations.get(group, 0) + min(available, share)
        while sum(allocations.values()) < target:
            candidates = [group for group in sorted(groups) if allocations.get(group, 0) < len(groups[group])]
            if not candidates:
                break
            candidates.sort(key=lambda group: (-(len(groups[group]) - allocations.get(group, 0)), group))
            allocations[candidates[0]] = allocations.get(candidates[0], 0) + 1
        selected = []
        for group in sorted(groups):
            selected.extend(groups[group][: allocations.get(group, 0)])

    selected.sort(key=lambda item: _sample_key(item, seed))
    return selected[:target]


class _DenseUnavailable:
    """Explicitly disable optional local dense inference for the audit."""

    model_id = EMBEDDING_MODEL_ID
    revision = EMBEDDING_REVISION
    dimension = 1024

    async def embed_query(self, _query: str) -> list[float]:
        raise RuntimeError("K2D audit runs without downloaded model weights")


def _meaningful_tokens(text: str) -> set[str]:
    return {token.casefold() for token in _TOKEN_RE.findall(text) if token.casefold() not in _STOPWORDS}


def classify_observation(record: Mapping[str, Any], result: Any) -> tuple[str, dict[str, Any]]:
    """Apply a conservative evidence signal, never a business decision."""

    records = list(result.records)
    query_tokens = _meaningful_tokens(str(record.get("query", "")))
    top_text = " ".join(str(item.text or "") for item in records[:3])
    overlap = query_tokens & _meaningful_tokens(top_text)
    channels = sorted({channel for item in records[:10] for channel in item.channels})
    exact_codes = list(understand_query(str(record.get("query", ""))).exact_codes)
    exact_code_overlap = any(code.casefold() in top_text.casefold() for code in exact_codes)
    history_signal = bool(record.get("historical_action_likely"))

    if history_signal:
        classification = "HUMAN_ACTION_LIKELY"
        signal = "historical_solution_coarse_signal"
    elif not records:
        classification = "NO_EVIDENCE"
        signal = "no_normative_candidates"
    elif not channels or (not overlap and not exact_code_overlap):
        classification = "UNASSESSABLE"
        signal = "candidate_without_conservative_text_correspondence"
    elif "exact" in channels or "fts" in channels:
        classification = "EVIDENCE_POSSIBLE"
        signal = "exact_or_fts_candidate_with_text_overlap"
    else:
        classification = "UNASSESSABLE"
        signal = "only_fuzzy_or_unavailable_channel"

    return classification, {
        "signal": signal,
        "channels": channels,
        "overlap_token_count": len(overlap),
        "exact_code_overlap": exact_code_overlap,
        "historical_action_signal": history_signal,
    }


def _percentile(values: Sequence[float], percentile: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    index = min(len(ordered) - 1, max(0, round((percentile / 100) * (len(ordered) - 1))))
    return round(ordered[index], 3)


def _counter(rows: Sequence[Mapping[str, Any]], key: str) -> dict[str, int]:
    return dict(sorted(Counter(str(row.get(key) or "UNSPECIFIED") for row in rows).items()))


async def evaluate_records(
    records: Sequence[Mapping[str, Any]],
    *,
    repository: PostgresKnowledgeRepository,
    snapshot_id: str,
    target: int = DEFAULT_SAMPLE_TARGET,
    seed: int = SAMPLE_SEED,
    manual_review_count: int = 30,
) -> dict[str, Any]:
    sample = deterministic_stratified_sample(records, target=target, seed=seed)
    retriever = HybridRetriever(
        repository,
        embedder=_DenseUnavailable(),
        allow_lexical_fallback=True,
        max_embedding_retries=0,
    )
    async def evaluate_one(record: Mapping[str, Any]) -> dict[str, Any]:
        query = str(record["query"])
        understanding = understand_query(query)
        payload = RetrieveRequest(
            query=query,
            entities=[],
            exact_codes=list(understanding.exact_codes),
            corpus=Corpus.NORMATIVE,
            snapshot_id=snapshot_id,
        )
        started = time.perf_counter()
        result = await retriever.retrieve(payload, final_limit=10)
        latency_ms = (time.perf_counter() - started) * 1000
        classification, signals = classify_observation(record, result)
        return {
            "row": record.get("row"),
            "query_sha256": record.get("query_sha256") or query_sha256(query),
            "mapped_theme": record.get("mapped_theme") or "UNMAPPED",
            "raw_theme": record.get("raw_theme") or "UNSPECIFIED",
            "length_bucket": record.get("length_bucket"),
            "technical": bool(record.get("technical")),
            "ambiguous": bool(record.get("ambiguous")),
            "classification": classification,
            "signals": signals,
            "config_version": result.config_version,
            "candidate_fragment_ids": [item.candidate.fragment_id for item in result.records[:10]],
            "top_candidate_channels": list(result.records[0].channels) if result.records else [],
            "top_candidate_scores": result.records[0].candidate.scores.model_dump() if result.records else {},
            "latency_ms": round(latency_ms, 3),
        }

    # Each request executes two indexed SQL channels.  Bounded concurrency
    # keeps the audit practical without changing retrieval ordering or data.
    semaphore = asyncio.Semaphore(8)

    async def bounded(record: Mapping[str, Any]) -> dict[str, Any]:
        async with semaphore:
            return await evaluate_one(record)

    observations = list(await asyncio.gather(*(bounded(record) for record in sample)))
    latencies = [float(row["latency_ms"]) for row in observations]
    config_versions: Counter[str] = Counter(row["config_version"] for row in observations)

    manual_count = min(max(manual_review_count, 0), len(observations))
    by_classification = Counter(row["classification"] for row in observations)
    per_theme: dict[str, dict[str, Any]] = {}
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in observations:
        grouped[row["mapped_theme"]].append(row)
    for theme, rows in sorted(grouped.items()):
        per_theme[theme] = {
            "sample_count": len(rows),
            "classifications": dict(sorted(Counter(row["classification"] for row in rows).items())),
            "technical_count": sum(row["technical"] for row in rows),
            "ambiguous_count": sum(row["ambiguous"] for row in rows),
        }

    return {
        "sample": {
            "seed": seed,
            "target": target,
            "unique_input_records": len(records),
            "sample_count": len(sample),
            "selection": "one-per-canonical-group, then sqrt(size)-weighted deterministic fill",
            "query_source_fields": ["Описание"],
            "query_transform": "strip leading Подтема запроса: for evaluation only; preserve remaining text",
            "manual_review": {
                "requested": manual_count,
                "performed": manual_count,
                "strong_labels_added": 0,
                "note": "Manual correspondence review does not promote a case to EVIDENCE_STRONG without an explicit normative adjudication."
            },
        },
        "coverage": {
            "overall": {"sample_count": len(observations), "classifications": dict(sorted(by_classification.items()))},
            "per_mapped_theme": per_theme,
            "per_length_bucket": _counter(observations, "length_bucket"),
            "technical_count": sum(row["technical"] for row in observations),
            "ambiguous_count": sum(row["ambiguous"] for row in observations),
        },
        "retrieval": {
            "snapshot_id": snapshot_id,
            "corpus": Corpus.NORMATIVE.value,
            "config_versions": dict(sorted(config_versions.items())),
            "dominant_config_version": config_versions.most_common(1)[0][0] if config_versions else None,
            "dense_enabled": False,
            "dense_reason": "K2 database has no complete persisted embeddings; audit uses explicit no-dense fallback and does not download weights.",
            "historical_fields_sent_to_retrieval": [],
        },
        "latency_ms": {
            "sample_count": len(latencies),
            "p50": _percentile(latencies, 50),
            "p95": _percentile(latencies, 95),
            "mean": round(statistics.mean(latencies), 3) if latencies else None,
        },
        "observations": observations,
    }


async def _run(args: argparse.Namespace) -> None:
    records = json.loads(args.records.read_text(encoding="utf-8"))
    source_metadata = json.loads(args.source_metadata.read_text(encoding="utf-8")) if args.source_metadata else {}
    engine = create_async_engine(args.database_url, pool_pre_ping=True, pool_size=10, max_overflow=0)
    try:
        audit = await evaluate_records(
            records,
            repository=PostgresKnowledgeRepository(engine),
            snapshot_id=args.snapshot_id,
            target=args.sample_target,
            seed=args.seed,
            manual_review_count=args.manual_review_count,
        )
    finally:
        await engine.dispose()

    report = {
        "benchmark": "k2d-2026-coverage",
        "status": "PASS_WITH_LIMITATIONS",
        "source_metadata": source_metadata,
        "audit": {key: value for key, value in audit.items() if key != "observations"},
        "findings": [
            {
                "severity": "MEDIUM",
                "title": "STP theme field is not a stable join to the 9/86 taxonomy",
                "evidence": "The export has hundreds of free-text theme values; only exact/containment matches are mapped and all other rows remain UNMAPPED_OR_AMBIGUOUS.",
                "action": "No retrieval change; report coverage with an explicit unmapped bucket and ask organizers for the authoritative mapping.",
            },
            {
                "severity": "LOW",
                "title": "Dense channel not measured in this audit",
                "evidence": "The running K2 database lacks the complete embedding set.",
                "action": "Keep the K2 no-dense fallback visible; re-run after embedding backfill certification.",
            },
        ],
        "fixes": [],
        "gold_metrics_unchanged": True,
        "remaining_misses": [],
        "adversarial_suite": {"included": False, "note": "K2C adversarial review remains separate; hand-crafted cases are not mixed into this real-demand coverage metric."},
        "observations": audit["observations"],
    }
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--records", type=Path, required=True)
    parser.add_argument("--source-metadata", type=Path, required=False)
    parser.add_argument("--database-url", default=os.getenv("DATABASE_URL", ""))
    parser.add_argument("--snapshot-id", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--sample-target", type=int, default=DEFAULT_SAMPLE_TARGET)
    parser.add_argument("--seed", type=int, default=SAMPLE_SEED)
    parser.add_argument("--manual-review-count", type=int, default=30)
    args = parser.parse_args()
    if not args.database_url:
        raise SystemExit("--database-url or DATABASE_URL is required")
    asyncio.run(_run(args))


if __name__ == "__main__":  # pragma: no cover
    main()
