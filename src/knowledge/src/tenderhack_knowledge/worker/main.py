import asyncio
import logging
import os

from tenderhack_knowledge.persistence.db import create_engine
from tenderhack_knowledge.quality.service import QualityRepository

logger = logging.getLogger(__name__)

DEFAULT_POLL_INTERVAL_SECONDS = 15.0


async def _run_once(repository: QualityRepository) -> None:
    pending = await repository.pending_evaluation_identities()
    for turn_id, revision in pending:
        await repository.evaluate_turn(turn_id, revision)
    if pending:
        # Only rebuild groups when there was new evaluation work — issue groups are a
        # derived read model over the same facts, not something feedback/completions
        # alone need to trigger a rebuild for.
        await repository.rebuild_issue_groups()


async def run_worker() -> None:
    """Periodically evaluates newly-pushed turns and rebuilds issue groups.

    Without this loop `/v0/quality/turns` pushes sit in `quality_cases` forever:
    `evaluate_turn_facts`/`build_issue_groups` (quality/service.py) are written and unit-tested
    but nothing ever called them outside of tests (quality.md's async-worker design).
    """

    engine = create_engine()
    if engine is None:
        # Dev/test environments without DATABASE_URL: idle rather than crash-looping.
        logger.warning("knowledge-worker: DATABASE_URL is not configured, idling")
        await asyncio.Event().wait()
        return

    interval = float(os.getenv("KNOWLEDGE_WORKER_INTERVAL_SECONDS", DEFAULT_POLL_INTERVAL_SECONDS))
    repository = QualityRepository(engine)
    try:
        while True:
            try:
                await _run_once(repository)
            except asyncio.CancelledError:
                raise
            except Exception:  # noqa: BLE001 - a transient DB hiccup must not kill the worker
                logger.exception("knowledge-worker: evaluation pass failed")
            await asyncio.sleep(interval)
    finally:
        await engine.dispose()


def run() -> None:
    from tenderhack_knowledge.observability.logging import configure_logging

    configure_logging()
    asyncio.run(run_worker())


if __name__ == "__main__":
    run()
