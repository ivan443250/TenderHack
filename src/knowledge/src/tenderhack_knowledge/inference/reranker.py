"""Lazy BAAI bge-reranker-v2-m3 adapter."""

from __future__ import annotations

import asyncio
import importlib
import math
import threading
from collections.abc import Callable, Mapping, Sequence
from dataclasses import dataclass
from typing import Any

from .errors import AdapterOutputError, ModelLoadError, OptionalDependencyError
from .model_refs import RERANKER_MODEL_ID, RERANKER_REVISION


@dataclass(frozen=True)
class _RerankerBundle:
    tokenizer: object
    model: object
    torch: object
    device: str


ComponentsFactory = Callable[..., _RerankerBundle]


def _default_components_factory(
    model_id: str,
    revision: str | None,
    device: str,
    dtype: str,
    *,
    local_files_only: bool = False,
) -> _RerankerBundle:
    try:
        torch = importlib.import_module("torch")
        transformers = importlib.import_module("transformers")
    except ImportError as exc:  # pragma: no cover - exercised in capability smoke
        raise OptionalDependencyError(
            "BGE reranking requires the optional 'inference' dependencies (torch, transformers)"
        ) from exc

    selected_device = device
    if selected_device == "auto":
        selected_device = "cuda" if torch.cuda.is_available() else "cpu"
    kwargs: dict[str, object] = {}
    if revision:
        kwargs["revision"] = revision
    if dtype != "auto":
        try:
            kwargs["torch_dtype"] = getattr(torch, dtype)
        except AttributeError as exc:
            raise ModelLoadError(f"Unsupported torch dtype: {dtype}") from exc
    try:
        tokenizer_kwargs = {"revision": revision} if revision else {}
        if local_files_only:
            tokenizer_kwargs["local_files_only"] = True
        tokenizer = transformers.AutoTokenizer.from_pretrained(model_id, **tokenizer_kwargs)
        if local_files_only:
            kwargs["local_files_only"] = True
        model = transformers.AutoModelForSequenceClassification.from_pretrained(model_id, **kwargs)
        to = getattr(model, "to", None)
        if callable(to):
            to(selected_device)
        eval_method = getattr(model, "eval", None)
        if callable(eval_method):
            eval_method()
    except Exception as exc:  # pragma: no cover - depends on optional runtime
        raise ModelLoadError(f"Unable to load reranker model {model_id}") from exc
    return _RerankerBundle(tokenizer=tokenizer, model=model, torch=torch, device=selected_device)


class BgeRerankerAdapter:
    """Score query/candidate pairs while preserving the candidate order."""

    model_id = RERANKER_MODEL_ID

    def __init__(
        self,
        *,
        model_id: str = model_id,
        revision: str | None = RERANKER_REVISION,
        device: str = "auto",
        dtype: str = "auto",
        batch_size: int = 8,
        max_length: int = 512,
        local_files_only: bool = False,
        components_factory: ComponentsFactory | None = None,
    ) -> None:
        if batch_size < 1:
            raise ValueError("batch_size must be positive")
        if max_length < 1:
            raise ValueError("max_length must be positive")
        self.model_id = model_id
        self.revision = revision
        self.device = device
        self.dtype = dtype
        self.batch_size = batch_size
        self.max_length = max_length
        self.local_files_only = local_files_only
        if components_factory is None:
            self._components_factory = lambda model, rev, selected_device, selected_dtype: _default_components_factory(
                model,
                rev,
                selected_device,
                selected_dtype,
                local_files_only=local_files_only,
            )
        else:
            self._components_factory = components_factory
        self._bundle: _RerankerBundle | None = None
        self._load_lock = threading.Lock()

    @property
    def loaded(self) -> bool:
        return self._bundle is not None

    @property
    def metadata(self) -> dict[str, object]:
        return {
            "model_id": self.model_id,
            "revision": self.revision,
            "device": self.device,
            "dtype": self.dtype,
            "batch_size": self.batch_size,
            "max_length": self.max_length,
            "score_semantics": "raw model score; not a probability",
            "local_files_only": self.local_files_only,
            "loaded": self.loaded,
        }

    def load(self) -> _RerankerBundle:
        if self._bundle is not None:
            return self._bundle
        with self._load_lock:
            if self._bundle is None:
                self._bundle = self._components_factory(self.model_id, self.revision, self.device, self.dtype)
        return self._bundle

    async def score(self, query: str, candidates: Sequence[str]) -> list[float]:
        values = list(candidates)
        if not values:
            return []
        if not isinstance(query, str) or any(not isinstance(candidate, str) for candidate in values):
            raise TypeError("query and candidates must be strings")
        bundle = await asyncio.to_thread(self.load)
        try:
            return await asyncio.to_thread(self._score_sync, bundle, query, values)
        except AdapterOutputError:
            raise
        except Exception as exc:  # pragma: no cover - depends on optional runtime
            raise AdapterOutputError("Reranker failed while scoring candidates") from exc

    def _score_sync(self, bundle: _RerankerBundle, query: str, candidates: list[str]) -> list[float]:
        scores: list[float] = []
        for start in range(0, len(candidates), self.batch_size):
            batch = candidates[start : start + self.batch_size]
            encoded = bundle.tokenizer(
                [query] * len(batch),
                batch,
                padding=True,
                truncation=True,
                max_length=self.max_length,
                return_tensors="pt",
            )
            if isinstance(encoded, Mapping):
                encoded = {
                    key: value.to(bundle.device) if callable(getattr(value, "to", None)) else value
                    for key, value in encoded.items()
                }
            no_grad = getattr(bundle.torch, "no_grad", None)
            context = no_grad() if callable(no_grad) else _NullContext()
            with context:
                output = bundle.model(**encoded) if isinstance(encoded, Mapping) else bundle.model(encoded)
            logits = getattr(output, "logits", output)
            batch_scores = _flatten_scores(logits)
            if len(batch_scores) != len(batch):
                raise AdapterOutputError(
                    f"Reranker returned {len(batch_scores)} scores for {len(batch)} candidates"
                )
            scores.extend(batch_scores)
        if len(scores) != len(candidates):
            raise AdapterOutputError(f"Reranker returned {len(scores)} scores for {len(candidates)} candidates")
        return scores


class _NullContext:
    def __enter__(self) -> "_NullContext":
        return self

    def __exit__(self, *_: object) -> None:
        return None


def _flatten_scores(logits: object) -> list[float]:
    detach = getattr(logits, "detach", None)
    if callable(detach):
        logits = detach()
    cpu = getattr(logits, "cpu", None)
    if callable(cpu):
        logits = cpu()
    tolist = getattr(logits, "tolist", None)
    values: object = tolist() if callable(tolist) else logits
    if isinstance(values, (int, float)):
        values = [values]
    if not isinstance(values, (tuple, list)):
        raise AdapterOutputError("Reranker output is not a sequence")
    flattened: list[float] = []
    for value in values:
        if isinstance(value, (tuple, list)):
            if len(value) != 1:
                raise AdapterOutputError("Reranker must return one scalar logit per candidate")
            value = value[0]
        try:
            numeric = float(value)
        except (TypeError, ValueError) as exc:
            raise AdapterOutputError("Reranker returned a non-numeric score") from exc
        if not math.isfinite(numeric):
            raise AdapterOutputError("Reranker returned a non-finite score")
        flattened.append(numeric)
    return flattened
