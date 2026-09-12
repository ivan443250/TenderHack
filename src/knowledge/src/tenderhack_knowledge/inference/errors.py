"""Errors raised by optional inference adapters and the local runtime client."""


class InferenceAdapterError(RuntimeError):
    """Base error for adapter lifecycle and output-contract failures."""


class OptionalDependencyError(InferenceAdapterError):
    """Raised when an adapter is used without its optional ML dependencies."""


class ModelLoadError(InferenceAdapterError):
    """Raised when a model cannot be initialized."""


class AdapterOutputError(InferenceAdapterError):
    """Raised when a model returns a value outside the adapter contract."""


class GeneratorClientError(InferenceAdapterError):
    """Raised for transport failures or malformed vLLM responses."""
