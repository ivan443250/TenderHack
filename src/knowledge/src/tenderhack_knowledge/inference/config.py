"""Configuration for the optional, explicitly started inference adapters."""

from __future__ import annotations

import os

from pydantic import BaseModel, Field, field_validator

from .model_refs import (
    EMBEDDING_DIMENSION,
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

    embedding_base_url: str = "http://embedding-inference:8080"
    embedding_dimension: int = Field(default=EMBEDDING_DIMENSION, ge=1)
    embedding_query_instruction_version: str = "giga_portal_support_v1"
    reranker_base_url: str = ""
    reranker_enabled: bool = False
    generator_base_url: str = "http://generator-inference:8080"
    generator_model_id: str = GENERATOR_MODEL_ID
    generator_revision: str | None = GENERATOR_REVISION
    generator_timeout_seconds: float = Field(default=30.0, gt=0, le=300)
    generator_temperature: float = Field(default=0.6, ge=0, le=2)
    generator_top_p: float = Field(default=0.95, gt=0, le=1)
    generator_top_k: int = Field(default=20, ge=1, le=200)
    generator_max_tokens: int = Field(default=512, ge=1, le=8192)

    @field_validator("embedding_dimension")
    @classmethod
    def _require_active_embedding_dimension(cls, value: int) -> int:
        if value != EMBEDDING_DIMENSION:
            raise ValueError(
                f"active Model Stack V2 requires embedding_dimension={EMBEDDING_DIMENSION}"
            )
        return value

    @classmethod
    def from_env(cls) -> "InferenceSettings":
        """Read settings without importing torch, transformers, or model code."""

        return cls(
            embedding_model_id=os.getenv("KNOWLEDGE_EMBEDDING_MODEL", cls.model_fields["embedding_model_id"].default),
            embedding_revision=os.getenv("KNOWLEDGE_EMBEDDING_REVISION", EMBEDDING_REVISION) or None,
            embedding_device=os.getenv("KNOWLEDGE_EMBEDDING_DEVICE", "auto"),
            embedding_dtype=os.getenv("KNOWLEDGE_EMBEDDING_DTYPE", "auto"),
            embedding_batch_size=_int_env("KNOWLEDGE_EMBEDDING_BATCH_SIZE", 8),
            embedding_base_url=os.getenv("KNOWLEDGE_EMBEDDING_BASE_URL", "http://embedding-inference:8080"),
            embedding_dimension=_int_env("KNOWLEDGE_EMBEDDING_DIMENSION", EMBEDDING_DIMENSION),
            embedding_query_instruction_version=os.getenv(
                "KNOWLEDGE_EMBEDDING_QUERY_INSTRUCTION_VERSION", "giga_portal_support_v1"
            ),
            reranker_model_id=os.getenv("KNOWLEDGE_RERANKER_MODEL", cls.model_fields["reranker_model_id"].default),
            reranker_revision=os.getenv("KNOWLEDGE_RERANKER_REVISION", RERANKER_REVISION) or None,
            reranker_device=os.getenv("KNOWLEDGE_RERANKER_DEVICE", "auto"),
            reranker_dtype=os.getenv("KNOWLEDGE_RERANKER_DTYPE", "auto"),
            reranker_batch_size=_int_env("KNOWLEDGE_RERANKER_BATCH_SIZE", 8),
            reranker_max_length=_int_env("KNOWLEDGE_RERANKER_MAX_LENGTH", 512),
            reranker_base_url=os.getenv("KNOWLEDGE_RERANKER_BASE_URL", ""),
            reranker_enabled=_bool_env("KNOWLEDGE_RERANKER_ENABLED", False),
            generator_base_url=os.getenv("KNOWLEDGE_GENERATOR_BASE_URL", "http://generator-inference:8080"),
            generator_model_id=os.getenv("KNOWLEDGE_GENERATOR_MODEL", cls.model_fields["generator_model_id"].default),
            generator_revision=os.getenv("KNOWLEDGE_GENERATOR_REVISION", GENERATOR_REVISION) or None,
            generator_timeout_seconds=_float_env("KNOWLEDGE_GENERATOR_TIMEOUT_SECONDS", 30.0),
            generator_temperature=_float_env("KNOWLEDGE_GENERATOR_TEMPERATURE", 0.6),
            generator_top_p=_float_env("KNOWLEDGE_GENERATOR_TOP_P", 0.95),
            generator_top_k=_int_env("KNOWLEDGE_GENERATOR_TOP_K", 20),
            generator_max_tokens=_int_env("KNOWLEDGE_GENERATOR_MAX_TOKENS", 512),
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


def _bool_env(name: str, default: bool) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default
    return raw.strip().lower() not in {"0", "false", "no", "off"}


InferenceConfig = InferenceSettings
