"""Narrow local model adapter protocols and opt-in inference adapters."""

from .embedding import Qwen3EmbeddingAdapter
from .giga import GigaEmbeddingAdapter
from .errors import (
    AdapterOutputError,
    GeneratorClientError,
    InferenceAdapterError,
    ModelLoadError,
    OptionalDependencyError,
)
from .generator import LocalOpenAIChatGenerator, VllmGeneratorClient
from .querit import QueritRerankerAdapter
from .reranker import BgeRerankerAdapter

__all__ = [
    "AdapterOutputError",
    "BgeRerankerAdapter",
    "GigaEmbeddingAdapter",
    "GeneratorClientError",
    "InferenceAdapterError",
    "LocalOpenAIChatGenerator",
    "ModelLoadError",
    "OptionalDependencyError",
    "Qwen3EmbeddingAdapter",
    "QueritRerankerAdapter",
    "VllmGeneratorClient",
]
