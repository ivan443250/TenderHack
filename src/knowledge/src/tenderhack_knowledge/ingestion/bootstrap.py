"""Deterministic multi-document normative-corpus bootstrap.

This module deliberately stops at the repository boundary.  Migrations,
settings and container startup are owned by the integration workstream; a
future caller can run migrations first and then call ``bootstrap_postgres``.
The complete manifest is prepared before the first write so a bad source or a
wrong corpus identity cannot create a partial snapshot.
"""

from __future__ import annotations

import asyncio
import json
from collections import Counter
from collections.abc import Iterable, Mapping, Sequence
from dataclasses import dataclass
from datetime import date
from pathlib import Path
from typing import Any, Protocol

from pydantic import BaseModel, ConfigDict, Field

from tenderhack_knowledge.conditions.cards import ConditionCard
from tenderhack_knowledge.conditions.repository import InMemoryConditionCardStore
from tenderhack_knowledge.conditions.seed import load_condition_card_seed
from tenderhack_knowledge.conditions.verification import ConditionCardAudit, verify_condition_cards

from .models import (
    Corpus,
    Document,
    DocumentVersion,
    IngestionRun,
    KnowledgeFragment,
    KnowledgeSnapshot,
    PreparedIngestion,
)
from .ids import make_snapshot_id
from .pipeline import IngestionPipeline
from .repository import InMemoryKnowledgeRepository, SourceResolver


class BootstrapError(RuntimeError):
    """Base error for an invalid or incomplete normative bootstrap."""


class CorpusIdentityMismatch(BootstrapError):
    """Raised when a manifest does not match the frozen corpus invariant."""


class MissingNormativeArtifact(BootstrapError):
    """Raised before persistence when a manifest source is unavailable."""


class InvalidManifest(BootstrapError):
    """Raised when a manifest entry is missing a required source identity."""


@dataclass(frozen=True)
class CorpusExpectation:
    """Frozen identity expected from the organizer's six normative manuals."""

    documents: int = 6
    pages: int = 830
    fragments: int = 3899


EXPECTED_NORMATIVE_CORPUS = CorpusExpectation()


class BootstrapResult(BaseModel):
    """Auditable result returned by either in-memory or PostgreSQL bootstrap."""

    model_config = ConfigDict(extra="forbid", frozen=True)

    status: str
    starting_manifest: str | None = None
    documents: tuple[dict[str, Any], ...] = ()
    document_count: int = Field(ge=0)
    pages: int = Field(ge=0)
    fragments: int = Field(ge=0)
    snapshot_id: str | None = None
    review_statuses: dict[str, int] = {}
    condition_cards: int = Field(ge=0)
    condition_card_versions: int = Field(ge=0)
    structurally_verified_cards: int = Field(ge=0)
    semantically_verified_cards: int = Field(ge=0)
    cards_requiring_review: tuple[str, ...] = ()
    condition_card_errors: tuple[dict[str, Any], ...] = ()
    source_resolution_checks: tuple[dict[str, Any], ...] = ()
    first_bootstrap: dict[str, Any] = {}
    repeated_bootstrap: dict[str, Any] = {}
    duplicate_count: int = Field(ge=0)
    historical_corpus_isolated: bool
    remaining_manual_steps: tuple[str, ...] = ()
    integration_work_required: tuple[str, ...] = ()
    limitations: tuple[str, ...] = ()


class AsyncBootstrapRepository(Protocol):
    async def register_document(self, document: Document) -> Document: ...

    async def store_version(
        self, version: DocumentVersion, fragments: Iterable[KnowledgeFragment], run: IngestionRun
    ) -> tuple[DocumentVersion, tuple[KnowledgeFragment, ...], IngestionRun, bool]: ...

    async def publish_snapshot(self, document_version_ids: Iterable[str], corpus: Corpus | str) -> KnowledgeSnapshot: ...

    async def snapshot_fragments(self, snapshot_id: str) -> tuple[KnowledgeFragment, ...]: ...

    async def store_condition_cards(self, cards: Iterable[ConditionCard]) -> int: ...

    async def get_snapshot(self, snapshot_id: str) -> KnowledgeSnapshot | None: ...


def load_manifest(path: str | Path) -> tuple[dict[str, Any], ...]:
    """Load and validate a JSON array without touching a repository."""

    manifest_path = Path(path).expanduser()
    if not manifest_path.is_file():
        raise MissingNormativeArtifact(str(manifest_path))
    try:
        payload = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise InvalidManifest(f"unable to read manifest {manifest_path}") from exc
    if not isinstance(payload, list) or not payload:
        raise InvalidManifest("manifest must be a non-empty JSON array")
    return _validate_entries(payload)


def _validate_entries(entries: Iterable[Mapping[str, Any]]) -> tuple[dict[str, Any], ...]:
    normalized: list[dict[str, Any]] = []
    names: set[str] = set()
    for index, raw in enumerate(entries):
        if not isinstance(raw, Mapping):
            raise InvalidManifest(f"entry {index} must be an object")
        source = raw.get("path")
        if not source:
            raise InvalidManifest(f"entry {index} requires path")
        path = Path(str(source)).expanduser()
        if not path.is_file():
            raise MissingNormativeArtifact(str(path))
        if path.suffix.casefold() != ".pdf":
            raise InvalidManifest(f"entry {index} is not a PDF: {path}")
        requested_corpus = str(raw.get("corpus") or Corpus.NORMATIVE.value).upper()
        if requested_corpus != Corpus.NORMATIVE.value:
            raise CorpusIdentityMismatch(
                f"normative bootstrap cannot include {requested_corpus} source at entry {index}"
            )
        filename = str(raw.get("original_filename") or path.name)
        reference = str(raw.get("source_reference") or "").strip()
        if not reference or reference == "<memory>":
            raise InvalidManifest(f"entry {index} requires a stable source_reference")
        if filename in names:
            raise CorpusIdentityMismatch(f"duplicate original_filename: {filename}")
        names.add(filename)
        declared_date = raw.get("declared_date")
        if declared_date is not None:
            try:
                date.fromisoformat(str(declared_date))
            except ValueError as exc:
                raise InvalidManifest(f"invalid declared_date for {filename}") from exc
        normalized.append(
            {
                **dict(raw),
                "path": path,
                "original_filename": filename,
                "source_reference": reference,
                "corpus": Corpus.NORMATIVE.value,
                "review_status": str(raw.get("review_status") or "PENDING_REVIEW"),
            }
        )
    return tuple(normalized)


def _prepare_entries(
    entries: Sequence[Mapping[str, Any]],
    *,
    extractor: Any = None,
    fragment_builder: Any = None,
    clock: Any = None,
) -> tuple[PreparedIngestion, ...]:
    pipeline = IngestionPipeline(
        InMemoryKnowledgeRepository(), extractor=extractor, fragment_builder=fragment_builder, clock=clock
    )
    prepared: list[PreparedIngestion] = []
    for entry in entries:
        prepared.append(
            pipeline.prepare(
                entry["path"],
                original_filename=entry["original_filename"],
                source_reference=entry["source_reference"],
                declared_version=entry.get("declared_version"),
                declared_date=entry.get("declared_date"),
                corpus=Corpus.NORMATIVE,
                review_status=entry["review_status"],
            )
        )
    return tuple(prepared)


def _validate_identity(
    entries: Sequence[Mapping[str, Any]], prepared: Sequence[PreparedIngestion], expectation: CorpusExpectation
) -> None:
    pages = sum(item.document_version.page_count for item in prepared)
    fragments = sum(len(item.fragments) for item in prepared)
    if len(prepared) != expectation.documents or pages != expectation.pages or fragments != expectation.fragments:
        raise CorpusIdentityMismatch(
            f"expected {expectation.documents}/{expectation.pages}/{expectation.fragments}, "
            f"got {len(prepared)}/{pages}/{fragments}"
        )
    for entry, item in zip(entries, prepared, strict=True):
        expected_pages = entry.get("expected_pages")
        if expected_pages is not None and item.document_version.page_count != int(expected_pages):
            raise CorpusIdentityMismatch(f"page count mismatch for {item.document.original_filename}")
        expected_fragments = entry.get("expected_fragments")
        if expected_fragments is not None and len(item.fragments) != int(expected_fragments):
            raise CorpusIdentityMismatch(f"fragment count mismatch for {item.document.original_filename}")


def _document_payload(item: PreparedIngestion) -> dict[str, Any]:
    version = item.document_version
    return {
        "filename": item.document.original_filename,
        "document_id": item.document.document_id,
        "document_version_id": version.document_version_id,
        "sha256": version.content_sha256,
        "pages": version.page_count,
        "fragments": len(item.fragments),
        "source_reference": version.source_reference,
        "review_status": version.review_status,
    }


def _sample_source_checks(
    resolver: SourceResolver,
    snapshot_id: str,
    fragments: Sequence[KnowledgeFragment],
    cards: Sequence[ConditionCard],
) -> tuple[dict[str, Any], ...]:
    candidates: list[tuple[str, str]] = []
    if fragments:
        candidates.append((fragments[0].fragment_id, "prose"))
    table = next((fragment for fragment in fragments if fragment.kind == "table_row"), None)
    if table is not None:
        candidates.append((table.fragment_id, "table"))
    if cards and cards[0].source_fragment_ids:
        candidates.append((cards[0].source_fragment_ids[0], "condition_card"))
    high_risk = next((card for card in cards if card.risk_level.value == "HIGH"), None)
    if high_risk and high_risk.source_fragment_ids:
        candidates.append((high_risk.source_fragment_ids[0], "high_risk"))
    checks: list[dict[str, Any]] = []
    seen: set[str] = set()
    for fragment_id, kind in candidates:
        if fragment_id in seen:
            continue
        seen.add(fragment_id)
        resolved = resolver.resolve(fragment_id, snapshot_id)
        checks.append(
            {
                "fragment_id": fragment_id,
                "sample": kind,
                "document_version_id": resolved.document_version_id,
                "page": resolved.page_start,
                "section": resolved.section,
                "anchor_present": resolved.source_anchor is not None,
                "text_present": bool(resolved.text.strip()),
                "review_status": resolved.review_status,
            }
        )
    return tuple(checks)


def _validate_cards_before_persist(
    cards: Sequence[ConditionCard], prepared: Sequence[PreparedIngestion], expected_snapshot_id: str
) -> None:
    """Reject invalid seed references before a snapshot can be published."""

    available = {fragment.fragment_id: fragment for item in prepared for fragment in item.fragments}
    for card in cards:
        if card.snapshot_id != expected_snapshot_id:
            raise CorpusIdentityMismatch("condition-card seed is pinned to a different snapshot")
        missing = sorted(set(card.source_fragment_ids) - set(available))
        if missing:
            raise BootstrapError(
                f"condition card {card.card_id}@{card.version} references missing fragment {missing[0]}"
            )
        if any(not available[fragment_id].text.strip() for fragment_id in card.source_fragment_ids):
            raise BootstrapError(f"condition card {card.card_id}@{card.version} references empty source text")
        for index, quote in enumerate(card.source_quotes):
            if index >= len(card.source_fragment_ids):
                raise BootstrapError(f"condition card {card.card_id}@{card.version} has too many source quotes")
            if quote and quote not in available[card.source_fragment_ids[index]].text:
                raise BootstrapError(f"condition card {card.card_id}@{card.version} quote is not source text")


def _audit_cards_sync(cards: Sequence[ConditionCard], repository: InMemoryKnowledgeRepository, snapshot_id: str) -> ConditionCardAudit:
    return asyncio.run(verify_condition_cards(cards, repository, snapshot_id))


def _result(
    *,
    prepared: Sequence[PreparedIngestion],
    snapshot: KnowledgeSnapshot,
    cards: Sequence[ConditionCard],
    audit: ConditionCardAudit,
    source_checks: Sequence[dict[str, Any]],
    first: Mapping[str, Any],
    repeated: Mapping[str, Any],
    duplicate_count: int,
    starting_manifest: str | None,
    semantic_review: Mapping[str, bool] | None = None,
) -> BootstrapResult:
    statuses = Counter(fragment.review_status for item in prepared for fragment in item.fragments)
    structurally_verified = len(audit.verified_cards)
    semantic_review = semantic_review or {}
    semantic_verified = tuple(
        f"{card.card_id}@{card.version}"
        for card in cards
        if semantic_review.get(f"{card.card_id}@{card.version}", False)
        and f"{card.card_id}@{card.version}" in audit.verified_cards
        and card.source_quotes
    )
    requiring_review = tuple(
        f"{card.card_id}@{card.version}"
        for card in cards
        if f"{card.card_id}@{card.version}" not in semantic_verified
    )
    return BootstrapResult(
        status="CERTIFIED_WITH_LIMITATIONS" if not audit.errors else "FAILED",
        starting_manifest=starting_manifest,
        documents=tuple(_document_payload(item) for item in prepared),
        document_count=len(prepared),
        pages=sum(item.document_version.page_count for item in prepared),
        fragments=sum(len(item.fragments) for item in prepared),
        snapshot_id=snapshot.snapshot_id,
        review_statuses=dict(sorted(statuses.items())),
        condition_cards=len(cards),
        condition_card_versions=len(cards),
        structurally_verified_cards=structurally_verified,
        semantically_verified_cards=len(semantic_verified),
        cards_requiring_review=requiring_review,
        condition_card_errors=tuple(error.model_dump(mode="json") for error in audit.errors),
        source_resolution_checks=tuple(source_checks),
        first_bootstrap=dict(first),
        repeated_bootstrap=dict(repeated),
        duplicate_count=duplicate_count,
        historical_corpus_isolated=True,
        remaining_manual_steps=(
            "review source semantics before promoting PENDING_REVIEW fragments",
            "provide explicit semantic attestations and source quotes for condition cards",
        ),
        integration_work_required=(
            "run migrations externally before bootstrap_postgres",
            "wire bootstrap and readiness into the Knowledge runtime (AI-5)",
            "resolve source links against the cited snapshot, not only the current snapshot",
        ),
        limitations=(
            "bootstrap does not execute migrations or container startup",
            "pdfplumber extraction quality still requires targeted table review",
            "semantic card verification is conservative and never inferred from IDs alone",
        ),
    )


def bootstrap_in_memory(
    manifest: str | Path | Iterable[Mapping[str, Any]],
    *,
    expectation: CorpusExpectation = EXPECTED_NORMATIVE_CORPUS,
    cards: Iterable[ConditionCard] | None = None,
    semantic_review: Mapping[str, bool] | None = None,
    extractor: Any = None,
    fragment_builder: Any = None,
    clock: Any = None,
) -> BootstrapResult:
    """Prepare, validate and idempotently publish one normative snapshot.

    The default expectation is the frozen six-manual corpus.  Tests may pass a
    smaller explicit expectation, but production callers must not weaken it.
    """

    if isinstance(manifest, (str, Path)):
        entries = load_manifest(manifest)
        manifest_name = str(manifest)
    else:
        entries = _validate_entries(manifest)
        manifest_name = None
    prepared = _prepare_entries(entries, extractor=extractor, fragment_builder=fragment_builder, clock=clock)
    _validate_identity(entries, prepared, expectation)
    repository = InMemoryKnowledgeRepository()
    card_values = tuple(cards) if cards is not None else load_condition_card_seed()
    expected_snapshot_id, _ = make_snapshot_id(
        Corpus.NORMATIVE.value, tuple(item.document_version.document_version_id for item in prepared)
    )
    _validate_cards_before_persist(card_values, prepared, expected_snapshot_id)

    def persist_once() -> tuple[KnowledgeSnapshot, tuple[KnowledgeFragment, ...], list[bool]]:
        idempotent: list[bool] = []
        for item in prepared:
            repository.register_document(item.document)
            _, _, _, already = repository.store_version(item.document_version, item.fragments, item.ingestion_run)
            idempotent.append(already)
        snapshot = repository.publish_snapshot(
            tuple(item.document_version.document_version_id for item in prepared), Corpus.NORMATIVE
        )
        return snapshot, repository.snapshot_fragments(snapshot.snapshot_id), idempotent

    first_snapshot, first_fragments, first_flags = persist_once()
    if first_snapshot.snapshot_id != expected_snapshot_id:  # pragma: no cover - repository identity invariant
        raise BootstrapError("repository returned an unexpected snapshot identity")
    card_store = InMemoryConditionCardStore()
    card_store.put_many(card_values)
    audit = _audit_cards_sync(card_values, repository, first_snapshot.snapshot_id)
    second_snapshot, second_fragments, second_flags = persist_once()
    if second_snapshot.snapshot_id != first_snapshot.snapshot_id:
        raise BootstrapError("repeated bootstrap produced a different snapshot")
    if tuple(fragment.fragment_id for fragment in first_fragments) != tuple(fragment.fragment_id for fragment in second_fragments):
        raise BootstrapError("repeated bootstrap changed fragment membership")
    duplicate_count = sum(1 for flag in second_flags if not flag)
    source_checks = _sample_source_checks(SourceResolver(repository), first_snapshot.snapshot_id, first_fragments, card_values)
    first = {"snapshot_id": first_snapshot.snapshot_id, "all_versions_new": not any(first_flags), "fragments": len(first_fragments)}
    repeated = {"snapshot_id": second_snapshot.snapshot_id, "all_versions_idempotent": all(second_flags), "fragments": len(second_fragments)}
    return _result(
        prepared=prepared,
        snapshot=first_snapshot,
        cards=card_values,
        audit=audit,
        source_checks=source_checks,
        first=first,
        repeated=repeated,
        duplicate_count=duplicate_count,
        starting_manifest=manifest_name,
        semantic_review=semantic_review,
    )


async def bootstrap_postgres(
    manifest: str | Path | Iterable[Mapping[str, Any]],
    repository: AsyncBootstrapRepository,
    *,
    expectation: CorpusExpectation = EXPECTED_NORMATIVE_CORPUS,
    cards: Iterable[ConditionCard] | None = None,
    semantic_review: Mapping[str, bool] | None = None,
    extractor: Any = None,
    fragment_builder: Any = None,
    clock: Any = None,
) -> BootstrapResult:
    """Async durable counterpart; migrations remain an external prerequisite."""

    if isinstance(manifest, (str, Path)):
        entries = load_manifest(manifest)
        manifest_name = str(manifest)
    else:
        entries = _validate_entries(manifest)
        manifest_name = None
    prepared = _prepare_entries(entries, extractor=extractor, fragment_builder=fragment_builder, clock=clock)
    _validate_identity(entries, prepared, expectation)
    card_values = tuple(cards) if cards is not None else load_condition_card_seed()
    expected_snapshot_id, _ = make_snapshot_id(
        Corpus.NORMATIVE.value, tuple(item.document_version.document_version_id for item in prepared)
    )
    _validate_cards_before_persist(card_values, prepared, expected_snapshot_id)

    async def persist_once() -> tuple[KnowledgeSnapshot, tuple[KnowledgeFragment, ...], list[bool]]:
        flags: list[bool] = []
        for item in prepared:
            await repository.register_document(item.document)
            _, _, _, already = await repository.store_version(item.document_version, item.fragments, item.ingestion_run)
            flags.append(already)
        snapshot = await repository.publish_snapshot(
            tuple(item.document_version.document_version_id for item in prepared), Corpus.NORMATIVE
        )
        return snapshot, await repository.snapshot_fragments(snapshot.snapshot_id), flags

    first_snapshot, first_fragments, first_flags = await persist_once()
    if first_snapshot.snapshot_id != expected_snapshot_id:  # pragma: no cover - repository identity invariant
        raise BootstrapError("repository returned an unexpected snapshot identity")
    await repository.store_condition_cards(card_values)
    audit = await verify_condition_cards(card_values, repository, first_snapshot.snapshot_id)
    second_snapshot, second_fragments, second_flags = await persist_once()
    if second_snapshot.snapshot_id != first_snapshot.snapshot_id:
        raise BootstrapError("repeated bootstrap produced a different snapshot")
    if tuple(fragment.fragment_id for fragment in first_fragments) != tuple(fragment.fragment_id for fragment in second_fragments):
        raise BootstrapError("repeated bootstrap changed fragment membership")
    statuses = Counter(fragment.review_status for fragment in first_fragments)
    semantic_review = semantic_review or {}
    semantic_verified = sum(
        1
        for card in card_values
        if semantic_review.get(f"{card.card_id}@{card.version}", False)
        and f"{card.card_id}@{card.version}" in audit.verified_cards
        and card.source_quotes
    )
    checks: list[dict[str, Any]] = []
    if first_fragments:
        for fragment in (first_fragments[0], next((f for f in first_fragments if f.kind == "table_row"), first_fragments[0])):
            checks.append({"fragment_id": fragment.fragment_id, "page": fragment.page_start, "text_present": bool(fragment.text.strip()), "review_status": fragment.review_status})
    return BootstrapResult(
        status="CERTIFIED_WITH_LIMITATIONS" if not audit.errors else "FAILED",
        starting_manifest=manifest_name,
        documents=tuple(_document_payload(item) for item in prepared),
        document_count=len(prepared),
        pages=sum(item.document_version.page_count for item in prepared),
        fragments=sum(len(item.fragments) for item in prepared),
        snapshot_id=first_snapshot.snapshot_id,
        review_statuses=dict(sorted(statuses.items())),
        condition_cards=len(card_values),
        condition_card_versions=len(card_values),
        structurally_verified_cards=len(audit.verified_cards),
        semantically_verified_cards=semantic_verified,
        cards_requiring_review=tuple(
            f"{card.card_id}@{card.version}"
            for card in card_values
            if not semantic_review.get(f"{card.card_id}@{card.version}", False)
        ),
        condition_card_errors=tuple(error.model_dump(mode="json") for error in audit.errors),
        source_resolution_checks=tuple(checks),
        first_bootstrap={"snapshot_id": first_snapshot.snapshot_id, "all_versions_new": not any(first_flags)},
        repeated_bootstrap={"snapshot_id": second_snapshot.snapshot_id, "all_versions_idempotent": all(second_flags)},
        duplicate_count=sum(1 for flag in second_flags if not flag),
        historical_corpus_isolated=True,
        remaining_manual_steps=("review source semantics before promoting PENDING_REVIEW fragments",),
        integration_work_required=("run migrations before this function", "wire startup/readiness in AI-5"),
        limitations=("migrations and container startup are intentionally out of scope",),
    )


__all__ = [
    "AsyncBootstrapRepository",
    "BootstrapError",
    "BootstrapResult",
    "CorpusExpectation",
    "CorpusIdentityMismatch",
    "EXPECTED_NORMATIVE_CORPUS",
    "InvalidManifest",
    "MissingNormativeArtifact",
    "bootstrap_in_memory",
    "bootstrap_postgres",
    "load_manifest",
]
