"""Import the organizer STP export into the isolated historical corpus."""

from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path

from tenderhack_knowledge.persistence.db import create_engine
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository

from .importer import import_stp_dataset


async def _run(path: Path) -> dict[str, object]:
    engine = create_engine()
    if engine is None:
        raise RuntimeError("DATABASE_URL is required")
    try:
        summary = await import_stp_dataset(path, PostgresKnowledgeRepository(engine))
        return {
            "dataset_sha256": summary.dataset_sha256,
            "rows_total": summary.rows_total,
            "rows_indexed": summary.rows_indexed,
            "pii_redactions": summary.pii_redactions,
            "snapshot_id": summary.snapshot_id,
            "document_version_id": summary.document_version_id,
            "idempotent": summary.idempotent,
        }
    finally:
        await engine.dispose()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    args = parser.parse_args(argv)
    result = asyncio.run(_run(args.source))
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
