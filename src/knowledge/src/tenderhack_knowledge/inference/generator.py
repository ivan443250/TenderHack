"""Runtime-neutral OpenAI-compatible client for the local llama.cpp server."""

from __future__ import annotations

from collections.abc import Mapping
from typing import Any

import httpx

from .errors import GeneratorClientError
from .model_refs import GENERATOR_MODEL_ID, GENERATOR_REVISION, GENERATOR_RUNTIME_FILE, LEGACY_GENERATOR_MODEL_ID, LEGACY_GENERATOR_REVISION
from .safety import strip_reasoning


class LocalOpenAIChatGenerator:
    """Call ``/v1/chat/completions`` without exposing a serving brand upstream."""

    model_id = GENERATOR_MODEL_ID

    def __init__(
        self,
        *,
        base_url: str = "http://generator-inference:8080",
        model_id: str = model_id,
        revision: str | None = GENERATOR_REVISION,
        timeout_seconds: float = 30.0,
        temperature: float = 0.6,
        top_p: float = 0.95,
        top_k: int = 20,
        max_tokens: int = 512,
        system_prompt: str = "Ты создаёшь только проверяемый структурированный черновик ответа по переданным источникам.",
        client: Any | None = None,
        transport: httpx.AsyncBaseTransport | None = None,
    ) -> None:
        if timeout_seconds <= 0 or max_tokens < 1 or not 0 <= temperature <= 2:
            raise ValueError("invalid generator runtime settings")
        if not 0 < top_p <= 1 or top_k < 1:
            raise ValueError("invalid generator sampling settings")
        self.base_url = base_url.rstrip("/")
        self.model_id = model_id
        self.revision = revision
        self.timeout_seconds = timeout_seconds
        self.temperature = temperature
        self.top_p = top_p
        self.top_k = top_k
        self.max_tokens = max_tokens
        self.system_prompt = system_prompt
        self._client = client
        self._transport = transport

    @property
    def endpoint(self) -> str:
        return f"{self.base_url}/v1/chat/completions"

    @property
    def metadata(self) -> dict[str, object]:
        return {
            "runtime": "llama.cpp OpenAI-compatible HTTP",
            "base_url": self.base_url,
            "endpoint": self.endpoint,
            "model_id": self.model_id,
            "revision": self.revision,
            "runtime_file": GENERATOR_RUNTIME_FILE,
            "temperature": self.temperature,
            "top_p": self.top_p,
            "top_k": self.top_k,
            "max_tokens": self.max_tokens,
            "reasoning_policy": "strip <think> before parsing; fail closed if unterminated",
        }

    async def draft(
        self,
        prompt: str,
        *,
        response_format: Mapping[str, object] | None = None,
    ) -> str:
        if not isinstance(prompt, str):
            raise TypeError("prompt must be a string")
        payload: dict[str, object] = {
            "model": self.model_id,
            "messages": [
                {"role": "system", "content": self.system_prompt},
                {"role": "user", "content": prompt},
            ],
            "temperature": self.temperature,
            "top_p": self.top_p,
            "top_k": self.top_k,
            "max_tokens": self.max_tokens,
        }
        if response_format is not None:
            payload["response_format"] = dict(response_format)
        response = await self._post(payload)
        return strip_reasoning(_extract_content(response))

    async def _post(self, payload: Mapping[str, object]) -> Mapping[str, object]:
        try:
            if self._client is not None:
                response = await self._client.post(self.endpoint, json=dict(payload))
            else:
                async with httpx.AsyncClient(timeout=self.timeout_seconds, transport=self._transport) as client:
                    response = await client.post(self.endpoint, json=dict(payload))
        except Exception as exc:
            raise GeneratorClientError(f"local generator request failed at {self.endpoint}") from exc
        status_code = getattr(response, "status_code", 200)
        if status_code >= 400:
            detail = getattr(response, "text", "")
            raise GeneratorClientError(f"local generator returned HTTP {status_code}: {detail[:500]}")
        try:
            body = response.json()
        except Exception as exc:
            raise GeneratorClientError("local generator returned invalid JSON") from exc
        if not isinstance(body, Mapping):
            raise GeneratorClientError("local generator response must be a JSON object")
        return body


def _extract_content(body: Mapping[str, object]) -> str:
    choices = body.get("choices")
    if not isinstance(choices, list) or not choices:
        raise GeneratorClientError("local generator response has no choices")
    first = choices[0]
    if not isinstance(first, Mapping):
        raise GeneratorClientError("local generator choice must be an object")
    message = first.get("message")
    if isinstance(message, Mapping) and isinstance(message.get("content"), str):
        return str(message["content"])
    raise GeneratorClientError("local generator choice has no message content")


class VllmGeneratorClient(LocalOpenAIChatGenerator):
    """Retired K0 compatibility client kept solely for historical tests.

    New application code must use :class:`LocalOpenAIChatGenerator`; this
    compatibility class is intentionally not exported by the active API.
    """

    def __init__(
        self,
        *args: object,
        model_id: str = LEGACY_GENERATOR_MODEL_ID,
        revision: str | None = LEGACY_GENERATOR_REVISION,
        completion_path: str = "/v1/completions",
        **kwargs: object,
    ) -> None:
        kwargs["model_id"] = model_id
        kwargs["revision"] = revision
        super().__init__(*args, **kwargs)
        self.completion_path = "/" + completion_path.lstrip("/")

    @property
    def endpoint(self) -> str:
        return f"{self.base_url}{self.completion_path}"

    async def draft(self, prompt: str, *, response_format: Mapping[str, object] | None = None) -> str:
        payload: dict[str, object] = {
            "model": self.model_id,
            "prompt": prompt,
            "temperature": self.temperature,
            "max_tokens": self.max_tokens,
        }
        if response_format is not None:
            payload["response_format"] = dict(response_format)
        return strip_reasoning(_extract_legacy_text(await self._post(payload)))


def _extract_legacy_text(body: Mapping[str, object]) -> str:
    choices = body.get("choices")
    if not isinstance(choices, list) or not choices:
        raise GeneratorClientError("vLLM response has no choices")
    first = choices[0]
    if not isinstance(first, Mapping):
        raise GeneratorClientError("vLLM response choice must be an object")
    text = first.get("text")
    if isinstance(text, str):
        return text
    raise GeneratorClientError("vLLM response choice has no text content")
