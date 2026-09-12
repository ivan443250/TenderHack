"""Configuration for the optional, explicitly started inference adapters."""

from __future__ import annotations

import os

from pydantic import BaseModel, Field

from .model_refs import (
    EMBEDDING_MODEL_ID,
    EMBEDDING_REVISION,
    GENERATOR_MODEL_ID,
    GENERATOR_REVISION,
    RERANKER_MODEL_ID,
    RERANKER_REVISION,
)


class InferenceSettings(BaseModel):
    """Environment-backed adapter settings with no model-loading side effects."""

    embedding_model_id: str = EMBEDDING_MODEL_ID
    embedding_revision: str | None = EMBEDDING_REVISION
    embedding_device: str = "auto"
    embedding_dtype: str = "auto"
    embedding_batch_size: int = Field(default=8, ge=1, le=256)

    reranker_model_id: str = RERANKER_MODEL_ID
    reranker_revision: str | None = RERANKER_REVISION
    reranker_device: str = "auto"
    reranker_dtype: str = "auto"
    reranker_batch_size: int = Field(default=8, ge=1, le=256)
    reranker_max_length: int = Field(default=512, ge=1, le=8192)

    vllm_base_url: str = "http://inference:8000"
    generator_model_id: str = GENERATOR_MODEL_ID
    generator_revision: str | None = GENERATOR_REVISION
    generator_timeout_seconds: float = Field(default=5.0, gt=0, le=300)
    generator_temperature: float = Field(default=0.1, ge=0, le=2)
    generator_max_tokens: int = Field(default=256, ge=1, le=8192)

    @classmethod
    def from_env(cls) -> "InferenceSettings":
        """Read settings without importing torch, transformers, or model code."""

        return cls(
            embedding_model_id=os.getenv("KNOWLEDGE_EMBEDDING_MODEL", cls.model_fields["embedding_model_id"].default),
            embedding_revision=os.getenv("KNOWLEDGE_EMBEDDING_REVISION", EMBEDDING_REVISION) or None,
            embedding_device=os.getenv("KNOWLEDGE_EMBEDDING_DEVICE", "auto"),
            embedding_dtype=os.getenv("KNOWLEDGE_EMBEDDING_DTYPE", "auto"),
            embedding_batch_size=_int_env("KNOWLEDGE_EMBEDDING_BATCH_SIZE", 8),
            reranker_model_id=os.getenv("KNOWLEDGE_RERANKER_MODEL", cls.model_fields["reranker_model_id"].default),
            reranker_revision=os.getenv("KNOWLEDGE_RERANKER_REVISION", RERANKER_REVISION) or None,
            reranker_device=os.getenv("KNOWLEDGE_RERANKER_DEVICE", "auto"),
            reranker_dtype=os.getenv("KNOWLEDGE_RERANKER_DTYPE", "auto"),
            reranker_batch_size=_int_env("KNOWLEDGE_RERANKER_BATCH_SIZE", 8),
            reranker_max_length=_int_env("KNOWLEDGE_RERANKER_MAX_LENGTH", 512),
            vllm_base_url=os.getenv("INFERENCE_BASE_URL", "http://inference:8000"),
            generator_model_id=os.getenv("KNOWLEDGE_GENERATOR_MODEL", cls.model_fields["generator_model_id"].default),
            generator_revision=os.getenv("KNOWLEDGE_GENERATOR_REVISION", GENERATOR_REVISION) or None,
            generator_timeout_seconds=_float_env("KNOWLEDGE_GENERATOR_TIMEOUT_SECONDS", 5.0),
            generator_temperature=_float_env("KNOWLEDGE_GENERATOR_TEMPERATURE", 0.1),
            generator_max_tokens=_int_env("KNOWLEDGE_GENERATOR_MAX_TOKENS", 256),
        )


def _int_env(name: str, default: int) -> int:
    raw = os.getenv(name)
    if raw is None:
        return default
    try:
        return int(raw)
    except ValueError as exc:
        raise ValueError(f"{name} must be an integer") from exc


def _float_env(name: str, default: float) -> float:
    raw = os.getenv(name)
    if raw is None:
        return default
    try:
        return float(raw)
    except ValueError as exc:
        raise ValueError(f"{name} must be a number") from exc


InferenceConfig = InferenceSettings
