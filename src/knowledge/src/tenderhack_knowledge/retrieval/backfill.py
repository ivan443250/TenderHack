"""Opt-in embedding backfill for one immutable knowledge snapshot.

This command is intentionally separate from service startup.  It processes
every snapshot fragment, writes model metadata with each vector and verifies
that no fragment is missing or mixed with another revision.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
from collections.abc import Sequence

from tenderhack_knowledge.inference.giga import GigaEmbeddingAdapter
from tenderhack_knowledge.inference.errors import EmbeddingRevisionMismatchError
from tenderhack_knowledge.inference.model_refs import EMBEDDING_DIMENSION, EMBEDDING_MODEL_ID, EMBEDDING_REVISION
from tenderhack_knowledge.persistence.db import create_engine
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository


async def backfill(
    repository: PostgresKnowledgeRepository,
    snapshot_id: str,
    *,
    adapter: object | None = None,
    batch_size: int = 8,
) -> dict[str, object]:
    if batch_size < 1:
        raise ValueError("batch_size must be positive")
    fragments = await repository.snapshot_fragments(snapshot_id)
    embedder = adapter if adapter is not None else GigaEmbeddingAdapter()
    if (
        getattr(embedder, "model_id", EMBEDDING_MODEL_ID) != EMBEDDING_MODEL_ID
        or getattr(embedder, "revision", EMBEDDING_REVISION) != EMBEDDING_REVISION
        or int(getattr(embedder, "dimension", EMBEDDING_DIMENSION)) != EMBEDDING_DIMENSION
    ):
        raise EmbeddingRevisionMismatchError("backfill adapter metadata does not match pinned Giga model")
    encode_documents = getattr(embedder, "embed_documents", None)
    if not callable(encode_documents):
        encode_documents = getattr(embedder, "embed", None)
    if not callable(encode_documents):
        raise RuntimeError("embedding adapter does not expose embed_documents")

    total = 0
    for start in range(0, len(fragments), batch_size):
        batch = fragments[start : start + batch_size]
        vectors = await encode_documents([fragment.text for fragment in batch])
        if len(vectors) != len(batch):
            raise RuntimeError(f"embedding returned {len(vectors)} rows for {len(batch)} fragments")
        await repository.store_embeddings(
            snapshot_id,
            {fragment.fragment_id: vector for fragment, vector in zip(batch, vectors, strict=True)},
            model_id=EMBEDDING_MODEL_ID,
            model_revision=EMBEDDING_REVISION,
            dimension=EMBEDDING_DIMENSION,
        )
        total += len(batch)
    stats = await repository.embedding_stats(
        snapshot_id,
        model_id=EMBEDDING_MODEL_ID,
        model_revision=EMBEDDING_REVISION,
        dimension=EMBEDDING_DIMENSION,
    )
    expected = len(fragments)
    if (
        stats["snapshot_fragments"] != expected
        or stats["embeddings"] != expected
        or stats["matching_metadata"] != expected
        or stats["non_finite"] != 0
        or not stats["identity_preserved"]
    ):
        raise RuntimeError(f"embedding backfill verification failed: {stats}")
    return {
        "snapshot_id": snapshot_id,
        "model_id": EMBEDDING_MODEL_ID,
        "model_revision": EMBEDDING_REVISION,
        "dimension": EMBEDDING_DIMENSION,
        "fragments_processed": total,
        "verification": stats,
    }


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--snapshot-id", required=True)
    parser.add_argument("--database-url", default=os.getenv("DATABASE_URL", ""))
    parser.add_argument("--batch-size", type=int, default=8)
    parser.add_argument("--base-url", default=os.getenv("KNOWLEDGE_EMBEDDING_BASE_URL", "http://embedding-inference:8080"))
    parser.add_argument("--timeout-seconds", type=float, default=10.0)
    args = parser.parse_args(argv)
    if not args.database_url:
        raise SystemExit("--database-url or DATABASE_URL is required")
    # create_engine reads the same environment setting; use an explicit
    # temporary override so the CLI argument remains useful and auditable.
    os.environ["DATABASE_URL"] = args.database_url
    engine = create_engine()
    if engine is None:
        raise SystemExit("database is not configured")
    async def _run() -> dict[str, object]:
        try:
            adapter = GigaEmbeddingAdapter(base_url=args.base_url, timeout_seconds=args.timeout_seconds)
            return await backfill(
                PostgresKnowledgeRepository(engine), args.snapshot_id, adapter=adapter, batch_size=args.batch_size
            )
        finally:
            await engine.dispose()

    result = asyncio.run(_run())
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
