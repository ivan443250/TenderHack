"""Convenient import surface for the selected inference adapters."""

from .embedding import Qwen3EmbeddingAdapter
from .errors import (
    AdapterOutputError,
    EmbeddingRevisionMismatchError,
    EmbeddingUnavailableError,
    GeneratorClientError,
    InferenceAdapterError,
    ModelLoadError,
    OptionalDependencyError,
    RerankerUnavailableError,
)
from .generator import VllmGeneratorClient
from .reranker import BgeRerankerAdapter

__all__ = [
    "AdapterOutputError",
    "BgeRerankerAdapter",
    "EmbeddingRevisionMismatchError",
    "EmbeddingUnavailableError",
    "GeneratorClientError",
    "InferenceAdapterError",
    "ModelLoadError",
    "OptionalDependencyError",
    "Qwen3EmbeddingAdapter",
    "RerankerUnavailableError",
    "VllmGeneratorClient",
]
