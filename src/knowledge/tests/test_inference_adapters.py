from __future__ import annotations

from types import SimpleNamespace

import httpx
import pytest

from tenderhack_knowledge.inference.config import InferenceSettings
from tenderhack_knowledge.inference.embedding import Qwen3EmbeddingAdapter
from tenderhack_knowledge.inference.errors import AdapterOutputError, GeneratorClientError, ModelLoadError
from tenderhack_knowledge.inference.generator import VllmGeneratorClient
from tenderhack_knowledge.inference.reranker import BgeRerankerAdapter, _RerankerBundle


class FakeEmbeddingModel:
    def __init__(self, dimension: int = 1024) -> None:
        self.dimension = dimension
        self.calls: list[list[str]] = []

    def get_sentence_embedding_dimension(self) -> int:
        return self.dimension

    def encode(self, texts: list[str], **_: object) -> list[list[float]]:
        self.calls.append(texts)
        return [[float(index) for index in range(self.dimension)] for _ in texts]


class PromptAwareEmbeddingModel(FakeEmbeddingModel):
    def __init__(self) -> None:
        super().__init__()
        self.roles: list[str] = []

    def encode_query(self, texts: list[str], **kwargs: object) -> list[list[float]]:
        self.roles.append("query")
        return self.encode(texts, **kwargs)

    def encode_document(self, texts: list[str], **kwargs: object) -> list[list[float]]:
        self.roles.append("document")
        return self.encode(texts, **kwargs)


@pytest.mark.asyncio
async def test_embedding_is_lazy_and_preserves_batch_shape() -> None:
    created: list[FakeEmbeddingModel] = []

    def factory(*_: object) -> FakeEmbeddingModel:
        model = FakeEmbeddingModel()
        created.append(model)
        return model

    adapter = Qwen3EmbeddingAdapter(model_factory=factory, batch_size=4)
    assert adapter.loaded is False
    assert created == []
    assert await adapter.embed([]) == []
    assert created == []

    result = await adapter.embed(["Привет", "Как подать заявку?"])
    assert len(result) == 2
    assert all(len(row) == 1024 for row in result)
    assert adapter.loaded is True
    assert len(created) == 1


@pytest.mark.asyncio
async def test_qwen_embedding_uses_query_and_document_conventions() -> None:
    model = PromptAwareEmbeddingModel()
    adapter = Qwen3EmbeddingAdapter(model_factory=lambda *_: model)
    await adapter.embed_query("query")
    await adapter.embed_documents(["document"])
    assert model.roles == ["query", "document"]


@pytest.mark.asyncio
async def test_embedding_dimension_guard() -> None:
    adapter = Qwen3EmbeddingAdapter(model_factory=lambda *_: FakeEmbeddingModel(1023))
    with pytest.raises(ModelLoadError, match="expected 1024"):
        await adapter.embed(["текст"])


@pytest.mark.asyncio
async def test_embedding_output_row_count_and_finite_values_are_checked() -> None:
    class BadModel(FakeEmbeddingModel):
        def encode(self, texts: list[str], **_: object) -> list[list[float]]:
            return [[float("nan")] * 1024 for _ in texts]

    adapter = Qwen3EmbeddingAdapter(model_factory=lambda *_: BadModel())
    with pytest.raises(AdapterOutputError, match="non-finite"):
        await adapter.embed(["текст"])


class FakeTensor:
    def __init__(self, values: object) -> None:
        self.values = values

    def to(self, _: str) -> "FakeTensor":
        return self

    def detach(self) -> "FakeTensor":
        return self

    def cpu(self) -> "FakeTensor":
        return self

    def tolist(self) -> object:
        return self.values


class FakeTorch:
    class _NoGrad:
        def __enter__(self) -> "FakeTorch._NoGrad":
            return self

        def __exit__(self, *_: object) -> None:
            return None

    def no_grad(self) -> "FakeTorch._NoGrad":
        return self._NoGrad()


class FakeTokenizer:
    def __call__(self, queries: list[str], candidates: list[str], **_: object) -> dict[str, FakeTensor]:
        del queries
        return {"input_ids": FakeTensor([[len(candidate)] for candidate in candidates])}


class FakeRerankerModel:
    def __call__(self, **kwargs: FakeTensor) -> SimpleNamespace:
        rows = kwargs["input_ids"].tolist()
        return SimpleNamespace(logits=FakeTensor([[float(row[0])] for row in rows]))


def reranker_factory(*_: object) -> _RerankerBundle:
    return _RerankerBundle(tokenizer=FakeTokenizer(), model=FakeRerankerModel(), torch=FakeTorch(), device="cpu")


@pytest.mark.asyncio
async def test_reranker_returns_one_score_per_candidate_in_order() -> None:
    adapter = BgeRerankerAdapter(components_factory=reranker_factory, batch_size=2)
    assert await adapter.score("запрос", []) == []
    scores = await adapter.score("запрос", ["a", "bbb", "cc"])
    assert scores == [1.0, 3.0, 2.0]


@pytest.mark.asyncio
async def test_reranker_is_lazy() -> None:
    created: list[object] = []

    def factory(*args: object) -> _RerankerBundle:
        created.append(args)
        return reranker_factory(*args)

    adapter = BgeRerankerAdapter(components_factory=factory)
    assert adapter.loaded is False
    assert await adapter.score("запрос", []) == []
    assert created == []


class FakeResponse:
    def __init__(self, body: object, status_code: int = 200) -> None:
        self.body = body
        self.status_code = status_code
        self.text = str(body)

    def json(self) -> object:
        return self.body


class FakeHttpClient:
    def __init__(self, response: FakeResponse | Exception) -> None:
        self.response = response
        self.calls: list[tuple[str, dict[str, object]]] = []

    async def post(self, url: str, *, json: dict[str, object]) -> FakeResponse:
        self.calls.append((url, json))
        if isinstance(self.response, Exception):
            raise self.response
        return self.response


@pytest.mark.asyncio
async def test_generator_client_uses_local_vllm_boundary() -> None:
    fake = FakeHttpClient(FakeResponse({"choices": [{"text": "Ответ"}]}))
    client = VllmGeneratorClient(client=fake, base_url="http://localhost:8000", max_tokens=12)
    assert await client.draft("тест") == "Ответ"
    assert fake.calls[0][0] == "http://localhost:8000/v1/completions"
    assert fake.calls[0][1]["model"] == "Qwen/Qwen3-4B-Instruct-2507"


@pytest.mark.asyncio
async def test_generator_client_rejects_malformed_response_and_transport_failure() -> None:
    malformed = VllmGeneratorClient(client=FakeHttpClient(FakeResponse({"choices": []})))
    with pytest.raises(GeneratorClientError, match="no choices"):
        await malformed.draft("тест")

    failed = VllmGeneratorClient(client=FakeHttpClient(httpx.ConnectError("offline")))
    with pytest.raises(GeneratorClientError, match="request failed"):
        await failed.draft("тест")


def test_inference_config_parses_without_loading_models(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("KNOWLEDGE_EMBEDDING_DEVICE", "cpu")
    monkeypatch.setenv("KNOWLEDGE_EMBEDDING_BATCH_SIZE", "4")
    monkeypatch.setenv("KNOWLEDGE_GENERATOR_TIMEOUT_SECONDS", "2.5")
    settings = InferenceSettings.from_env()
    assert settings.embedding_device == "cpu"
    assert settings.embedding_batch_size == 4
    assert settings.generator_timeout_seconds == 2.5
    assert settings.generator_model_id == "empero-ai/Qwen3.8-4B-Distill"
    assert settings.embedding_revision is not None
    assert settings.reranker_revision is not None
    assert settings.generator_revision is not None
