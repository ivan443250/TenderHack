"""Convenient import surface for the selected inference adapters."""

from .embedding import Qwen3EmbeddingAdapter
from .giga import GigaEmbeddingAdapter
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
from .generator import LocalOpenAIChatGenerator, VllmGeneratorClient
from .querit import QueritRerankerAdapter
from .reranker import BgeRerankerAdapter

__all__ = [
    "AdapterOutputError",
    "BgeRerankerAdapter",
    "GigaEmbeddingAdapter",
    "EmbeddingRevisionMismatchError",
    "EmbeddingUnavailableError",
    "GeneratorClientError",
    "InferenceAdapterError",
    "LocalOpenAIChatGenerator",
    "ModelLoadError",
    "OptionalDependencyError",
    "Qwen3EmbeddingAdapter",
    "QueritRerankerAdapter",
    "RerankerUnavailableError",
    "VllmGeneratorClient",
]
