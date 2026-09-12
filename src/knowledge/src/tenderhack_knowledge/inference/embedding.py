"""Lazy Qwen3-Embedding adapter.

The optional SentenceTransformers dependency is imported only when ``load`` (or
the first ``embed`` call) is explicitly reached.  Importing this module is safe
for the ordinary lightweight knowledge service and test suite.
"""

from __future__ import annotations

import asyncio
import importlib
import math
import threading
from collections.abc import Callable, Sequence
from typing import Any

from .errors import AdapterOutputError, ModelLoadError, OptionalDependencyError
from .model_refs import EMBEDDING_MODEL_ID, EMBEDDING_REVISION


ModelFactory = Callable[..., object]


def _default_model_factory(
    model_id: str,
    revision: str | None,
    device: str,
    dtype: str,
    *,
    local_files_only: bool = False,
) -> object:
    try:
        module = importlib.import_module("sentence_transformers")
    except ImportError as exc:  # pragma: no cover - exercised in capability smoke
        raise OptionalDependencyError(
            "Qwen3 embedding requires the optional 'inference' dependencies "
            "(sentence-transformers, torch, transformers)"
        ) from exc

    kwargs: dict[str, object] = {}
    if revision:
        kwargs["revision"] = revision
    if device != "auto":
        kwargs["device"] = device
    if dtype != "auto":
        kwargs["model_kwargs"] = {"torch_dtype": dtype}
    if local_files_only:
        kwargs["local_files_only"] = True
    try:
        return module.SentenceTransformer(model_id, **kwargs)
    except Exception as exc:  # pragma: no cover - depends on optional runtime
        raise ModelLoadError(f"Unable to load embedding model {model_id}") from exc


class Qwen3EmbeddingAdapter:
    """Embed Russian or multilingual text with a strict 1024-wide output."""

    model_id = EMBEDDING_MODEL_ID
    dimension = 1024

    def __init__(
        self,
        *,
        model_id: str = model_id,
        revision: str | None = EMBEDDING_REVISION,
        device: str = "auto",
        dtype: str = "auto",
        batch_size: int = 8,
        local_files_only: bool = False,
        model_factory: ModelFactory | None = None,
    ) -> None:
        if batch_size < 1:
            raise ValueError("batch_size must be positive")
        self.model_id = model_id
        self.revision = revision
        self.device = device
        self.dtype = dtype
        self.batch_size = batch_size
        self.local_files_only = local_files_only
        if model_factory is None:
            self._model_factory = lambda model, rev, selected_device, selected_dtype: _default_model_factory(
                model, rev, selected_device, selected_dtype, local_files_only=local_files_only
            )
        else:
            self._model_factory = model_factory
        self._model: object | None = None
        self._load_lock = threading.Lock()

    @property
    def loaded(self) -> bool:
        return self._model is not None

    @property
    def metadata(self) -> dict[str, object]:
        """Stable observability metadata without forcing model initialization."""

        return {
            "model_id": self.model_id,
            "revision": self.revision,
            "device": self.device,
            "dtype": self.dtype,
            "dimension": self.dimension,
            "local_files_only": self.local_files_only,
            "loaded": self.loaded,
        }

    def load(self) -> object:
        """Load the model exactly once; this method is an explicit lifecycle edge."""

        if self._model is not None:
            return self._model
        with self._load_lock:
            if self._model is None:
                model = self._model_factory(self.model_id, self.revision, self.device, self.dtype)
                get_dimension = getattr(model, "get_sentence_embedding_dimension", None)
                if callable(get_dimension):
                    observed = get_dimension()
                    if observed is not None and int(observed) != self.dimension:
                        raise ModelLoadError(
                            f"Embedding model returned dimension {observed}; expected {self.dimension}"
                        )
                self._model = model
        return self._model

    async def embed(self, texts: Sequence[str]) -> list[list[float]]:
        """Embed document text using the model's document convention.

        ``embed`` is retained for backwards compatibility with the K0 adapter
        and is deliberately document-oriented.  Retrieval code should use
        :meth:`embed_query` for the user query and :meth:`embed_documents` for
        persisted fragments so Qwen3's asymmetric query/document prompts are
        not accidentally mixed.
        """

        return await self._embed(texts, role="document")

    async def embed_query(self, text: str) -> list[float]:
        """Encode one query with Qwen3's official query instruction."""

        rows = await self._embed([text], role="query")
        return rows[0]

    async def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        """Encode persisted fragments with Qwen3's document instruction."""

        return await self._embed(texts, role="document")

    async def _embed(self, texts: Sequence[str], *, role: str) -> list[list[float]]:
        values = list(texts)
        if not values:
            return []
        if any(not isinstance(text, str) for text in values):
            raise TypeError("embedding input must contain only strings")
        model = await asyncio.to_thread(self.load)
        try:
            kwargs = {
                "batch_size": self.batch_size,
                "convert_to_numpy": True,
                "show_progress_bar": False,
                "normalize_embeddings": False,
            }
            role_method = getattr(model, f"encode_{role}", None)
            if callable(role_method):
                encoded = await asyncio.to_thread(role_method, values, **kwargs)
            else:
                # Older SentenceTransformers versions expose only ``encode``;
                # Qwen3 documents the prompt_name convention for that API.
                kwargs["prompt_name"] = role
                try:
                    encoded = await asyncio.to_thread(getattr(model, "encode"), values, **kwargs)
                except TypeError:
                    # Keep compatibility with a minimal local test/runtime
                    # wrapper that does not expose prompt_name.  The official
                    # Qwen3 adapter path uses encode_query/encode_document.
                    kwargs.pop("prompt_name", None)
                    encoded = await asyncio.to_thread(getattr(model, "encode"), values, **kwargs)
        except Exception as exc:  # pragma: no cover - depends on optional runtime
            raise AdapterOutputError("Embedding model failed while encoding input") from exc

        rows = _as_rows(encoded)
        if len(rows) != len(values):
            raise AdapterOutputError(f"Embedding returned {len(rows)} rows for {len(values)} inputs")
        result: list[list[float]] = []
        for index, row in enumerate(rows):
            try:
                floats = [float(value) for value in row]
            except (TypeError, ValueError) as exc:
                raise AdapterOutputError(f"Embedding row {index} contains a non-numeric value") from exc
            if len(floats) != self.dimension:
                raise AdapterOutputError(
                    f"Embedding row {index} has dimension {len(floats)}; expected {self.dimension}"
                )
            if not all(math.isfinite(value) for value in floats):
                raise AdapterOutputError(f"Embedding row {index} contains a non-finite value")
            result.append(floats)
        return result


def _as_rows(encoded: object) -> list[list[Any]]:
    tolist = getattr(encoded, "tolist", None)
    value = tolist() if callable(tolist) else encoded
    if isinstance(value, (tuple, list)):
        if value and isinstance(value[0], (int, float)):
            return [list(value)]
        return [list(row) for row in value]
    raise AdapterOutputError("Embedding output is not a sequence of rows")
