"""Errors raised by optional inference adapters and the local runtime client."""


class InferenceAdapterError(RuntimeError):
    """Base error for adapter lifecycle and output-contract failures."""


class OptionalDependencyError(InferenceAdapterError):
    """Raised when an adapter is used without its optional ML dependencies."""


class ModelLoadError(InferenceAdapterError):
    """Raised when a model cannot be initialized."""


class AdapterOutputError(InferenceAdapterError):
    """Raised when a model returns a value outside the adapter contract."""


class EmbeddingUnavailableError(InferenceAdapterError):
    """Raised when dense retrieval cannot obtain a usable local embedding."""


class EmbeddingRevisionMismatchError(InferenceAdapterError):
    """Raised when persisted/query embeddings use different model metadata."""


class RerankerUnavailableError(InferenceAdapterError):
    """Raised when the optional local reranker cannot be used."""


class GeneratorClientError(InferenceAdapterError):
    """Raised for transport failures or malformed local-runtime responses."""
