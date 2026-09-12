from typing import Protocol, Sequence


class Embedder(Protocol):
    async def embed(self, texts: Sequence[str]) -> list[list[float]]: ...


class Reranker(Protocol):
    async def score(self, query: str, candidates: Sequence[str]) -> list[float]: ...


class Generator(Protocol):
    async def draft(self, prompt: str) -> str: ...


class Verifier(Protocol):
    async def verify(self, claim: str, evidence: Sequence[str]) -> bool: ...
