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
from tenderhack_knowledge.settings.config import Settings


@pytest.fixture(autouse=True)
def _clear_optional_inference_token(monkeypatch: pytest.MonkeyPatch) -> None:
    """Keep legacy fake clients exercising the unauthenticated path."""

    monkeypatch.delenv("KNOWLEDGE_INFERENCE_BEARER_TOKEN", raising=False)


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


class _AuthEmbeddingClient:
    def __init__(self, body: object | None = None) -> None:
        self.headers: list[dict[str, str] | None] = []
        self.body = body

    async def post(self, _url: str, *, json: dict[str, object], headers: dict[str, str] | None = None) -> _Response:
        del json
        self.headers.append(headers)
        if self.body is not None:
            return _Response(self.body)
        value = [1 / (2048**0.5)] * 2048
        return _Response({"data": [{"index": 0, "embedding": value}]})


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
async def test_giga_optional_bearer_header_and_secret_redaction(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("KNOWLEDGE_INFERENCE_BEARER_TOKEN", raising=False)
    unauthenticated_client = _AuthEmbeddingClient()
    await GigaEmbeddingAdapter(client=unauthenticated_client).embed_documents(["document"])
    assert unauthenticated_client.headers == [None]

    token = "runpod-secret-token"
    authenticated_client = _AuthEmbeddingClient()
    adapter = GigaEmbeddingAdapter(client=authenticated_client, bearer_token=token)
    await adapter.embed_documents(["document"])
    assert authenticated_client.headers == [{"Authorization": f"Bearer {token}"}]
    assert token not in repr(adapter.metadata)
    assert token not in str(adapter.metadata)

    monkeypatch.setenv("KNOWLEDGE_INFERENCE_BEARER_TOKEN", token)
    env_client = _AuthEmbeddingClient()
    await GigaEmbeddingAdapter(client=env_client).embed_documents(["document"])
    assert env_client.headers == [{"Authorization": f"Bearer {token}"}]

    class ErrorClient:
        async def post(
            self,
            _url: str,
            *,
            json: dict[str, object],
            headers: dict[str, str] | None = None,
        ) -> _Response:
            del json, headers
            raise RuntimeError(f"upstream echoed {token}")

    with pytest.raises(Exception) as error:
        await GigaEmbeddingAdapter(client=ErrorClient(), bearer_token=token).embed_documents(["document"])
    assert token not in str(error.value)
    assert error.value.__cause__ is None


def test_inference_bearer_setting_is_optional(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("KNOWLEDGE_INFERENCE_BEARER_TOKEN", raising=False)
    assert Settings.from_env().inference_bearer_token is None
    monkeypatch.setenv("KNOWLEDGE_INFERENCE_BEARER_TOKEN", "configured-token")
    settings = Settings.from_env()
    assert settings.inference_bearer_token == "configured-token"
    assert "configured-token" not in repr(settings)


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
        self.headers: list[dict[str, str] | None] = []

    async def post(
        self,
        url: str,
        *,
        json: dict[str, object],
        headers: dict[str, str] | None = None,
    ) -> _Response:
        self.calls.append((url, json))
        self.headers.append(headers)
        return _Response(self.body)


@pytest.mark.asyncio
async def test_local_generator_uses_chat_completions_and_reasoning_boundary() -> None:
    fake = _GeneratorClient({"choices": [{"message": {"content": "<think>secret</think>{\"claims\":[]}"}}]})
    generator = LocalOpenAIChatGenerator(client=fake, base_url="http://generator", max_tokens=321)
    assert await generator.draft("evidence") == '{"claims":[]}'
    assert fake.calls[0][0] == "http://generator/v1/chat/completions"
    assert fake.calls[0][1]["messages"][0]["role"] == "system"
    assert fake.calls[0][1]["max_tokens"] == 321
    assert "<think>" not in await generator.draft("evidence", max_tokens=300)
    assert fake.calls[1][1]["max_tokens"] == 300
    assert "<think>" not in await generator.draft("evidence")
    assert fake.calls[2][1]["max_tokens"] == 321
    assert fake.headers == [None, None, None]


def test_local_generator_rejects_output_budget_above_hard_cap() -> None:
    with pytest.raises(ValueError, match="between 1 and 800"):
        LocalOpenAIChatGenerator(max_tokens=801)


@pytest.mark.asyncio
async def test_local_generator_rejects_per_call_budget_above_hard_cap() -> None:
    fake = _GeneratorClient({"choices": [{"message": {"content": "answer"}}]})
    generator = LocalOpenAIChatGenerator(client=fake)
    with pytest.raises(ValueError, match="between 1 and 800"):
        await generator.draft("evidence", max_tokens=801)
    assert fake.calls == []


@pytest.mark.asyncio
async def test_generator_optional_bearer_header_and_error_redaction(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("KNOWLEDGE_INFERENCE_BEARER_TOKEN", raising=False)
    token = "runpod-generator-secret"
    fake = _GeneratorClient({"choices": [{"message": {"content": "answer"}}]})
    generator = LocalOpenAIChatGenerator(client=fake, base_url="http://generator", bearer_token=token)
    assert await generator.draft("evidence") == "answer"
    assert fake.headers == [{"Authorization": f"Bearer {token}"}]
    assert token not in repr(generator.metadata)
    assert token not in str(generator.metadata)

    monkeypatch.setenv("KNOWLEDGE_INFERENCE_BEARER_TOKEN", token)
    env_fake = _GeneratorClient({"choices": [{"message": {"content": "answer"}}]})
    assert await LocalOpenAIChatGenerator(client=env_fake).draft("evidence") == "answer"
    assert env_fake.headers == [{"Authorization": f"Bearer {token}"}]

    class ErrorClient:
        async def post(
            self,
            _url: str,
            *,
            json: dict[str, object],
            headers: dict[str, str] | None = None,
        ) -> _Response:
            del json, headers
            response = _Response({})
            response.status_code = 502
            response.text = f"upstream echoed {token}"
            return response

    with pytest.raises(Exception) as error:
        await LocalOpenAIChatGenerator(client=ErrorClient(), bearer_token=token).draft("evidence")
    assert token not in str(error.value)


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
