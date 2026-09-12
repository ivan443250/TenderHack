"""Deterministic identifiers for documents, versions, fragments and snapshots."""

from __future__ import annotations

import hashlib
import json
import unicodedata
from collections.abc import Iterable


def normalize_source_name(value: str) -> str:
    """Normalize Unicode/path separators without assuming ASCII filenames."""

    normalized = unicodedata.normalize("NFC", value).replace("\\", "/")
    return "/".join(part for part in normalized.split("/") if part not in {"", "."})


def sha256_bytes(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def text_hash(text: str) -> str:
    canonical = unicodedata.normalize("NFC", text).replace("\r\n", "\n").strip()
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def _digest(*parts: object) -> str:
    encoded = json.dumps(parts, ensure_ascii=False, separators=(",", ":"), sort_keys=False).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def make_document_id(original_filename: str, source_reference: str) -> str:
    # A filesystem path is provenance, not identity: the same named document
    # copied to another safe workspace must retain its version/fragment IDs.
    # Content changes are captured by ``make_document_version_id`` below.
    del source_reference
    return f"doc_{_digest('document', normalize_source_name(original_filename))[:32]}"


def make_document_version_id(
    document_id: str,
    content_sha256: str,
    declared_version: str | None,
    declared_date: str | None,
) -> str:
    return f"ver_{_digest('document-version', document_id, content_sha256, declared_version, declared_date)[:32]}"


def make_fragment_id(
    document_version_id: str,
    page_start: int,
    page_end: int,
    section: str | None,
    anchor: str,
    fragment_text_hash: str,
) -> str:
    return f"frag_{_digest('fragment', document_version_id, page_start, page_end, section, anchor, fragment_text_hash)[:32]}"


def make_snapshot_id(corpus: str, document_version_ids: Iterable[str]) -> tuple[str, str]:
    ordered = tuple(sorted(set(document_version_ids)))
    manifest_hash = _digest("snapshot-manifest", corpus, ordered)
    return f"snap_{manifest_hash[:32]}", manifest_hash


def make_ingestion_run_id(corpus: str, document_version_id: str, extractor_version: str | None) -> str:
    return f"run_{_digest('ingestion-run', corpus, document_version_id, extractor_version)[:32]}"
