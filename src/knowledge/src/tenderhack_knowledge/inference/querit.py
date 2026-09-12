"""Querit-specific reranker adapter.

Querit is a custom ``AutoModel`` implementation.  It returns a ``score``
tensor, not a conventional SequenceClassification ``logits`` field; this
adapter deliberately does not guess a logits/probability interpretation.
"""

from __future__ import annotations

import asyncio
import importlib
import math
import threading
from collections.abc import Mapping, Sequence
from typing import Any

from .errors import AdapterOutputError, ModelLoadError, OptionalDependencyError, RerankerUnavailableError
from .model_refs import RERANKER_MODEL_ID, RERANKER_REVISION


class QueritRerankerAdapter:
    """Score query/passage pairs with the pinned custom Querit model."""

    model_id = RERANKER_MODEL_ID
    revision = RERANKER_REVISION

    def __init__(
        self,
        *,
        model_id: str = model_id,
        revision: str | None = revision,
        device: str = "auto",
        dtype: str = "auto",
        batch_size: int = 4,
        max_length: int = 512,
        local_files_only: bool = False,
        components_factory: Any | None = None,
    ) -> None:
        if batch_size < 1 or max_length < 1:
            raise ValueError("batch_size and max_length must be positive")
        self.model_id = model_id
        self.revision = revision
        self.device = device
        self.dtype = dtype
        self.batch_size = batch_size
        self.max_length = max_length
        self.local_files_only = local_files_only
        self._components_factory = components_factory or self._default_factory
        self._bundle: tuple[object, object, object, str] | None = None
        self._load_lock = threading.Lock()

    @property
    def loaded(self) -> bool:
        return self._bundle is not None

    @property
    def metadata(self) -> dict[str, object]:
        return {
            "runtime": "Transformers custom Querit AutoModel",
            "model_id": self.model_id,
            "revision": self.revision,
            "device": self.device,
            "dtype": self.dtype,
            "batch_size": self.batch_size,
            "max_length": self.max_length,
            "score_semantics": "raw Querit relevance score; not a probability",
            "trust_remote_code": True,
            "local_files_only": self.local_files_only,
            "loaded": self.loaded,
        }

    def load(self) -> tuple[object, object, object, str]:
        if self._bundle is not None:
            return self._bundle
        with self._load_lock:
            if self._bundle is None:
                self._bundle = self._components_factory()
        return self._bundle

    async def score(self, query: str, candidates: Sequence[str]) -> list[float]:
        values = list(candidates)
        if not isinstance(query, str) or any(not isinstance(value, str) for value in values):
            raise TypeError("query and candidates must be strings")
        if not values:
            return []
        bundle = await asyncio.to_thread(self.load)
        try:
            return await asyncio.to_thread(self._score_sync, bundle, query, values)
        except (AdapterOutputError, RerankerUnavailableError):
            raise
        except Exception as exc:
            raise AdapterOutputError("Querit failed while scoring input pairs") from exc

    def _default_factory(self) -> tuple[object, object, object, str]:
        try:
            torch = importlib.import_module("torch")
            transformers = importlib.import_module("transformers")
        except ImportError as exc:
            raise OptionalDependencyError("Querit reranking requires torch and transformers") from exc
        selected_device = self.device
        if selected_device == "auto":
            selected_device = "cuda" if torch.cuda.is_available() else "cpu"
        tokenizer_kwargs: dict[str, object] = {"trust_remote_code": True}
        model_kwargs: dict[str, object] = {"trust_remote_code": True}
        if self.revision:
            tokenizer_kwargs["revision"] = self.revision
            model_kwargs["revision"] = self.revision
        if self.local_files_only:
            tokenizer_kwargs["local_files_only"] = True
            model_kwargs["local_files_only"] = True
        if self.dtype != "auto":
            try:
                model_kwargs["torch_dtype"] = getattr(torch, self.dtype)
            except AttributeError as exc:
                raise ModelLoadError(f"unsupported torch dtype: {self.dtype}") from exc
        try:
            tokenizer = transformers.AutoTokenizer.from_pretrained(self.model_id, **tokenizer_kwargs)
            model = transformers.AutoModel.from_pretrained(self.model_id, **model_kwargs)
            to = getattr(model, "to", None)
            if callable(to):
                to(selected_device)
            eval_method = getattr(model, "eval", None)
            if callable(eval_method):
                eval_method()
        except Exception as exc:
            raise ModelLoadError(f"unable to load Querit model {self.model_id}") from exc
        return tokenizer, model, torch, selected_device

    def _score_sync(self, bundle: tuple[object, object, object, str], query: str, candidates: list[str]) -> list[float]:
        tokenizer, model, torch, device = bundle
        scores: list[float] = []
        for start in range(0, len(candidates), self.batch_size):
            batch = candidates[start : start + self.batch_size]
            encoded = tokenizer(
                [query] * len(batch),
                batch,
                padding=True,
                truncation=True,
                max_length=self.max_length,
                return_tensors="pt",
            )
            if isinstance(encoded, Mapping):
                encoded = {
                    key: value.to(device) if callable(getattr(value, "to", None)) else value
                    for key, value in encoded.items()
                }
            no_grad = getattr(torch, "no_grad", None)
            context = no_grad() if callable(no_grad) else _NullContext()
            with context:
                output = model(**encoded) if isinstance(encoded, Mapping) else model(encoded)
            score = output.get("score") if isinstance(output, Mapping) else getattr(output, "score", None)
            if score is None:
                raise AdapterOutputError("Querit output does not contain a score field")
            batch_scores = _flatten_scores(score)
            if len(batch_scores) != len(batch):
                raise AdapterOutputError(f"Querit returned {len(batch_scores)} scores for {len(batch)} candidates")
            scores.extend(batch_scores)
        return scores


class _NullContext:
    def __enter__(self) -> "_NullContext":
        return self

    def __exit__(self, *_: object) -> None:
        return None


def _flatten_scores(value: object) -> list[float]:
    detach = getattr(value, "detach", None)
    if callable(detach):
        value = detach()
    cpu = getattr(value, "cpu", None)
    if callable(cpu):
        value = cpu()
    tolist = getattr(value, "tolist", None)
    values = tolist() if callable(tolist) else value
    if isinstance(values, (int, float)):
        values = [values]
    if not isinstance(values, (list, tuple)):
        raise AdapterOutputError("Querit score is not a sequence")
    flattened: list[float] = []
    for item in values:
        if isinstance(item, (list, tuple)):
            if len(item) != 1:
                raise AdapterOutputError("Querit must return one scalar score per candidate")
            item = item[0]
        try:
            numeric = float(item)
        except (TypeError, ValueError) as exc:
            raise AdapterOutputError("Querit returned a non-numeric score") from exc
        if not math.isfinite(numeric):
            raise AdapterOutputError("Querit returned a non-finite score")
        flattened.append(numeric)
    return flattened
