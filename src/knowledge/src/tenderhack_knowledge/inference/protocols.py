from collections.abc import Mapping
from typing import Protocol, Sequence


class Embedder(Protocol):
    """Model-neutral embedding boundary (query and document paths differ)."""

    async def embed(self, texts: Sequence[str]) -> list[list[float]]: ...

    async def embed_query(self, text: str) -> list[float]: ...

    async def embed_documents(self, texts: Sequence[str]) -> list[list[float]]: ...


class Reranker(Protocol):
    async def score(self, query: str, candidates: Sequence[str]) -> list[float]: ...


class Generator(Protocol):
    async def draft(self, prompt: str, *, response_format: Mapping[str, object] | None = None) -> str: ...


class Verifier(Protocol):
    async def verify(self, claim: str, evidence: Sequence[str]) -> bool: ...
