"""Reproducible local corpus ingestion command."""

from __future__ import annotations

import argparse
import asyncio
import json
import sys
from pathlib import Path
from typing import Any

from tenderhack_knowledge.persistence.db import create_engine
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository

from .pipeline import IngestionPipeline
from .repository import InMemoryKnowledgeRepository


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Ingest knowledge PDFs into the normative corpus")
    parser.add_argument("--manifest", type=Path, help="JSON manifest with path and optional metadata")
    parser.add_argument("--source", action="append", type=Path, help="PDF source; may be repeated")
    parser.add_argument("--corpus", default="NORMATIVE", choices=("NORMATIVE", "HISTORICAL"))
    parser.add_argument("--publish-snapshot", action="store_true")
    return parser


def _entries(args: argparse.Namespace) -> list[dict[str, Any]]:
    if args.manifest and args.source:
        raise ValueError("use --manifest or --source, not both")
    if args.manifest:
        payload = json.loads(args.manifest.read_text(encoding="utf-8"))
        if not isinstance(payload, list) or not payload:
            raise ValueError("manifest must be a non-empty JSON array")
        entries = payload
    elif args.source:
        entries = [{"path": str(path)} for path in args.source]
    else:
        raise ValueError("provide --manifest or at least one --source")
    normalized: list[dict[str, Any]] = []
    for entry in entries:
        if not isinstance(entry, dict) or not entry.get("path"):
            raise ValueError("each manifest entry requires a path")
        path = Path(str(entry["path"])).expanduser()
        if not path.is_file():
            raise FileNotFoundError(path)
        normalized.append({**entry, "path": path})
    return normalized


async def _run(args: argparse.Namespace, entries: list[dict[str, Any]]) -> dict[str, Any]:
    memory_repository = InMemoryKnowledgeRepository()
    pipeline = IngestionPipeline(memory_repository)
    results = []
    for entry in entries:
        path = entry["path"]
        results.append(
            pipeline.ingest(
                path,
                original_filename=entry.get("original_filename") or path.name,
                source_reference=entry.get("source_reference") or path.name,
                declared_version=entry.get("declared_version"),
                declared_date=entry.get("declared_date"),
                corpus=args.corpus,
                review_status=entry.get("review_status", "PENDING_REVIEW"),
            )
        )

    engine = create_engine()
    durable_repository = PostgresKnowledgeRepository(engine) if engine is not None else None
    persisted = durable_repository is not None
    snapshot = None
    if durable_repository is not None:
        for result in results:
            await durable_repository.register_document(result.document)
            await durable_repository.store_version(result.document_version, result.fragments, result.ingestion_run)
        if args.publish_snapshot:
            snapshot = await durable_repository.publish_snapshot(
                [result.document_version.document_version_id for result in results],
                args.corpus,
            )
    elif args.publish_snapshot:
        snapshot = memory_repository.publish_snapshot(
            [result.document_version.document_version_id for result in results],
            args.corpus,
        )

    documents = [
        {
            "filename": result.document.original_filename,
            "document_id": result.document.document_id,
            "document_version_id": result.document_version.document_version_id,
            "pages": result.document_version.page_count,
            "fragments": len(result.fragments),
            "warnings": list(result.ingestion_run.warnings),
        }
        for result in results
    ]
    fragment_count = sum(item["fragments"] for item in documents)
    return {
        "documents": documents,
        "document_count": len(documents),
        "fragment_count": fragment_count,
        "snapshot": {
            "snapshot_id": snapshot.snapshot_id if snapshot else None,
            "corpus": args.corpus,
            "document_count": len(results),
            "fragment_count": fragment_count,
            "persisted_to_postgres": persisted and snapshot is not None,
        },
    }


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    try:
        entries = _entries(args)
        summary = asyncio.run(_run(args, entries))
    except Exception as exc:
        print(f"ingestion failed: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(summary, ensure_ascii=False, indent=2, default=str))
    return 0


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
