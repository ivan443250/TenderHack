"""Model-neutral HTTP adapter for the local Giga llama.cpp endpoint.

The Knowledge runtime only depends on the OpenAI-compatible embeddings
protocol.  It does not import llama.cpp or download model files.
"""

from __future__ import annotations

import math
from collections.abc import Mapping, Sequence
from typing import Any

import httpx

from .errors import AdapterOutputError, EmbeddingUnavailableError
from .model_refs import EMBEDDING_DIMENSION, EMBEDDING_MODEL_ID, EMBEDDING_REVISION


GIGA_QUERY_INSTRUCTION_VERSION = "giga_portal_support_v1"
GIGA_QUERY_INSTRUCTION = (
    "Найди фрагменты официальных инструкций Портала для поставщиков, "
    "которые содержат информацию, необходимую для ответа на запрос пользователя."
)


def format_giga_query(text: str) -> str:
    """Return the versioned asymmetric query format exactly once."""

    if not isinstance(text, str):
        raise TypeError("query must be a string")
    prefix = f"Instruct: {GIGA_QUERY_INSTRUCTION}\nQuery: "
    return text if text.startswith(prefix) else prefix + text


class GigaEmbeddingAdapter:
    """Call a local ``/v1/embeddings`` endpoint and validate Giga vectors."""

    model_id = EMBEDDING_MODEL_ID
    revision = EMBEDDING_REVISION
    dimension = EMBEDDING_DIMENSION

    def __init__(
        self,
        *,
        base_url: str = "http://embedding-inference:8080",
        model_id: str = model_id,
        revision: str | None = revision,
        timeout_seconds: float = 10.0,
        client: Any | None = None,
    ) -> None:
        if timeout_seconds <= 0:
            raise ValueError("timeout_seconds must be positive")
        self.base_url = base_url.rstrip("/")
        self.model_id = model_id
        self.revision = revision
        self.timeout_seconds = timeout_seconds
        self._client = client

    @property
    def endpoint(self) -> str:
        return f"{self.base_url}/v1/embeddings"

    @property
    def metadata(self) -> dict[str, object]:
        return {
            "runtime": "llama.cpp OpenAI-compatible HTTP",
            "base_url": self.base_url,
            "endpoint": self.endpoint,
            "model_id": self.model_id,
            "revision": self.revision,
            "device": "remote-local-runtime",
            "dtype": "runtime-defined",
            "dimension": self.dimension,
            "query_instruction_version": GIGA_QUERY_INSTRUCTION_VERSION,
            "normalized": True,
        }

    def load(self) -> "GigaEmbeddingAdapter":
        """Keep the generic smoke lifecycle without loading weights locally."""

        return self

    async def embed_query(self, text: str) -> list[float]:
        rows = await self._embed([format_giga_query(text)])
        return rows[0]

    async def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        values = list(texts)
        if any(not isinstance(text, str) for text in values):
            raise TypeError("document inputs must contain only strings")
        return await self._embed(values)

    async def embed(self, texts: Sequence[str]) -> list[list[float]]:
        """Compatibility alias for document embedding."""

        return await self.embed_documents(texts)

    async def _embed(self, inputs: Sequence[str]) -> list[list[float]]:
        if not inputs:
            return []
        payload = {"model": self.model_id, "input": list(inputs)}
        try:
            if self._client is not None:
                response = await self._client.post(self.endpoint, json=payload)
            else:
                async with httpx.AsyncClient(timeout=self.timeout_seconds) as client:
                    response = await client.post(self.endpoint, json=payload)
        except Exception as exc:
            raise EmbeddingUnavailableError(f"local embedding request failed at {self.endpoint}") from exc
        status_code = getattr(response, "status_code", 200)
        if status_code >= 400:
            raise EmbeddingUnavailableError(f"local embedding returned HTTP {status_code}")
        try:
            body = response.json()
        except Exception as exc:
            raise AdapterOutputError("local embedding returned invalid JSON") from exc
        rows = _extract_rows(body)
        if len(rows) != len(inputs):
            raise AdapterOutputError(f"embedding returned {len(rows)} rows for {len(inputs)} inputs")
        for index, row in enumerate(rows):
            _validate_normalized_vector(row, index)
        return rows


def _extract_rows(body: object) -> list[list[float]]:
    if not isinstance(body, Mapping) or not isinstance(body.get("data"), list):
        raise AdapterOutputError("embedding response must contain a data array")
    values: list[tuple[int, list[float]]] = []
    for item in body["data"]:
        if not isinstance(item, Mapping) or not isinstance(item.get("embedding"), list):
            raise AdapterOutputError("embedding data item has no embedding array")
        index = item.get("index", len(values))
        try:
            values.append((int(index), [float(value) for value in item["embedding"]]))
        except (TypeError, ValueError) as exc:
            raise AdapterOutputError("embedding contains non-numeric values") from exc
    indexes = [index for index, _ in values]
    if sorted(indexes) != list(range(len(values))):
        raise AdapterOutputError("embedding data indexes must be a unique contiguous range")
    values.sort(key=lambda item: item[0])
    return [row for _, row in values]


def _validate_normalized_vector(vector: Sequence[float], index: int) -> None:
    if len(vector) != EMBEDDING_DIMENSION:
        raise AdapterOutputError(
            f"embedding row {index} has dimension {len(vector)}; expected {EMBEDDING_DIMENSION}"
        )
    if any(not math.isfinite(value) for value in vector):
        raise AdapterOutputError(f"embedding row {index} contains a non-finite value")
    norm = math.sqrt(sum(value * value for value in vector))
    if not math.isfinite(norm) or abs(norm - 1.0) > 0.02:
        raise AdapterOutputError(f"embedding row {index} is not normalized (L2 norm={norm:.6f})")
