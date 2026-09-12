from __future__ import annotations

import json
from types import SimpleNamespace
from pathlib import Path

import pytest

from tenderhack_knowledge.inference.giga import GigaEmbeddingAdapter, format_giga_query
from tenderhack_knowledge.inference.config import InferenceSettings
from tenderhack_knowledge.inference.generator import LocalOpenAIChatGenerator
from tenderhack_knowledge.inference.querit import QueritRerankerAdapter
from tenderhack_knowledge.inference.safety import strip_reasoning
from tenderhack_knowledge.inference.errors import EmbeddingRevisionMismatchError
from tenderhack_knowledge.inference.smoke import run_smoke
from tenderhack_knowledge.persistence.repository import _validate_embedding_metadata


class _Response:
    status_code = 200
    text = ""

    def __init__(self, body: object) -> None:
        self.body = body

    def json(self) -> object:
        return self.body


class _EmbeddingClient:
    def __init__(self) -> None:
        self.payloads: list[dict[str, object]] = []

    async def post(self, _url: str, *, json: dict[str, object]) -> _Response:
        self.payloads.append(json)
        value = [1 / (2048**0.5)] * 2048
        return _Response({"data": [{"index": index, "embedding": value} for index, _ in enumerate(json["input"]) ]})


@pytest.mark.asyncio
async def test_giga_query_instruction_once_and_document_plain() -> None:
    client = _EmbeddingClient()
    adapter = GigaEmbeddingAdapter(client=client, base_url="http://embedding")
    formatted = format_giga_query("как загрузить МЧД")
    assert format_giga_query(formatted) == formatted
    query_vector = await adapter.embed_query("как загрузить МЧД")
    document_vector = (await adapter.embed_documents(["текст нормативного фрагмента"]))[0]
    assert len(query_vector) == len(document_vector) == 2048
    assert client.payloads[0]["input"][0].count("Instruct:") == 1
    assert "Instruct:" not in client.payloads[1]["input"][0]


@pytest.mark.asyncio
async def test_giga_rejects_wrong_dimension_and_non_normalized_output() -> None:
    class BadClient:
        async def post(self, _url: str, *, json: dict[str, object]) -> _Response:
            del json
            return _Response({"data": [{"index": 0, "embedding": [0.0] * 2047}]})

    with pytest.raises(Exception, match="2048"):
        await GigaEmbeddingAdapter(client=BadClient()).embed_query("query")


@pytest.mark.asyncio
async def test_giga_rejects_missing_or_duplicate_response_indexes() -> None:
    class BadIndexClient:
        async def post(self, _url: str, *, json: dict[str, object]) -> _Response:
            del json
            value = [1 / (2048**0.5)] * 2048
            return _Response(
                {"data": [{"index": 0, "embedding": value}, {"index": 0, "embedding": value}]}
            )

    with pytest.raises(Exception, match="indexes"):
        await GigaEmbeddingAdapter(client=BadIndexClient()).embed_documents(["a", "b"])


class _Tensor:
    def __init__(self, values: object) -> None:
        self.values = values

    def to(self, _: str) -> "_Tensor":
        return self

    def detach(self) -> "_Tensor":
        return self

    def cpu(self) -> "_Tensor":
        return self

    def tolist(self) -> object:
        return self.values


class _Torch:
    class _NoGrad:
        def __enter__(self) -> "_Torch._NoGrad":
            return self

        def __exit__(self, *_: object) -> None:
            return None

    def no_grad(self) -> "_Torch._NoGrad":
        return self._NoGrad()


class _Tokenizer:
    def __call__(self, queries: list[str], candidates: list[str], **_: object) -> dict[str, _Tensor]:
        del queries, candidates
        return {"input_ids": _Tensor([[1], [1]])}


class _QueritModel:
    def __call__(self, **_: object) -> dict[str, _Tensor]:
        return {"score": _Tensor([[2.0], [1.0]])}


def _querit_components() -> tuple[object, object, object, str]:
    return _Tokenizer(), _QueritModel(), _Torch(), "cpu"


@pytest.mark.asyncio
async def test_querit_consumes_custom_score_field() -> None:
    adapter = QueritRerankerAdapter(components_factory=_querit_components)
    assert await adapter.score("query", ["relevant", "weak"]) == [2.0, 1.0]


class _GeneratorClient:
    def __init__(self, body: object) -> None:
        self.body = body
        self.calls: list[tuple[str, dict[str, object]]] = []

    async def post(self, url: str, *, json: dict[str, object]) -> _Response:
        self.calls.append((url, json))
        return _Response(self.body)


@pytest.mark.asyncio
async def test_local_generator_uses_chat_completions_and_reasoning_boundary() -> None:
    fake = _GeneratorClient({"choices": [{"message": {"content": "<think>secret</think>{\"claims\":[]}"}}]})
    generator = LocalOpenAIChatGenerator(client=fake, base_url="http://generator")
    assert await generator.draft("evidence") == '{"claims":[]}'
    assert fake.calls[0][0] == "http://generator/v1/chat/completions"
    assert fake.calls[0][1]["messages"][0]["role"] == "system"
    assert "<think>" not in await generator.draft("evidence")


def test_reasoning_boundary_fails_closed_when_unterminated() -> None:
    with pytest.raises(Exception, match="unterminated"):
        strip_reasoning("<think>private")


def test_model_manifest_has_pinned_v2_roles_without_fictional_querit_artifact() -> None:
    manifest_path = Path(__file__).parents[1] / "model-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    assert manifest["manifest_version"] == "model-stack-v2"
    embedding = manifest["roles"]["embedding"]
    assert embedding["embedding_dimension"] == 2048
    assert embedding["quantization"] == "Q8_0"
    assert embedding["runtime_size_bytes"] == 3354067168
    assert embedding["runtime_sha256"]
    reranker = manifest["roles"]["reranker"]
    assert reranker["runtime_type"] == "TBD_UNVERIFIED"
    assert reranker["runtime_repo"] is None
    generator = manifest["roles"]["generator"]
    assert generator["quantization"] == "Q6_K"
    assert generator["runtime_sha256"] is None


def test_active_persistence_rejects_legacy_embedding_metadata() -> None:
    with pytest.raises(EmbeddingRevisionMismatchError):
        _validate_embedding_metadata(
            "Qwen/Qwen3-Embedding-0.6B",
            "legacy",
            1024,
        )


def test_active_settings_reject_legacy_embedding_dimension() -> None:
    with pytest.raises(ValueError, match="embedding_dimension=2048"):
        InferenceSettings(embedding_dimension=1024)


def test_embedding_migration_invalidates_legacy_rows_and_targets_2048() -> None:
    migration = (
        Path(__file__).parents[1]
        / "migrations"
        / "versions"
        / "0007_giga_2048_embeddings.py"
    ).read_text(encoding="utf-8")
    assert 'revision = "0007_giga_2048_embeddings"' in migration
    assert 'down_revision = "0006_condition_cards"' in migration
    assert "DELETE FROM kb_fragment_embeddings" in migration
    assert "vector(2048)" in migration
    assert "dimension = 2048" in migration
    assert "DROP TABLE" not in migration.split("def upgrade", 1)[1].split("def downgrade", 1)[0]


@pytest.mark.asyncio
async def test_default_capability_smoke_is_non_contacting() -> None:
    report = await run_smoke()
    assert report["status"] == "PASS_WITH_LIMITATIONS"
    assert report["heavyweight_execution"] is False
