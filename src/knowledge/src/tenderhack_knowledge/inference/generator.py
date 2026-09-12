"""HTTP boundary for a locally hosted vLLM OpenAI-compatible server."""

from __future__ import annotations

from collections.abc import Mapping
from typing import Any

import httpx

from .errors import GeneratorClientError
from .model_refs import GENERATOR_MODEL_ID, GENERATOR_REVISION


class VllmGeneratorClient:
    """A small, dependency-light client; it never imports or starts a model."""

    model_id = GENERATOR_MODEL_ID

    def __init__(
        self,
        *,
        base_url: str = "http://inference:8000",
        model_id: str = model_id,
        revision: str | None = GENERATOR_REVISION,
        timeout_seconds: float = 5.0,
        temperature: float = 0.1,
        max_tokens: int = 256,
        client: Any | None = None,
        transport: httpx.AsyncBaseTransport | None = None,
        completion_path: str = "/v1/completions",
    ) -> None:
        if timeout_seconds <= 0:
            raise ValueError("timeout_seconds must be positive")
        if max_tokens < 1:
            raise ValueError("max_tokens must be positive")
        self.base_url = base_url.rstrip("/")
        self.model_id = model_id
        self.revision = revision
        self.timeout_seconds = timeout_seconds
        self.temperature = temperature
        self.max_tokens = max_tokens
        self.completion_path = "/" + completion_path.lstrip("/")
        self._client = client
        self._transport = transport

    @property
    def endpoint(self) -> str:
        return f"{self.base_url}{self.completion_path}"

    @property
    def metadata(self) -> dict[str, object]:
        return {
            "runtime": "vLLM OpenAI-compatible HTTP",
            "base_url": self.base_url,
            "endpoint": self.endpoint,
            "model_id": self.model_id,
            "revision": self.revision,
            "temperature": self.temperature,
            "max_tokens": self.max_tokens,
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
            "prompt": prompt,
            "temperature": self.temperature,
            "max_tokens": self.max_tokens,
        }
        if response_format is not None:
            payload["response_format"] = dict(response_format)
        response = await self._post(payload)
        return _extract_text(response)

    async def _post(self, payload: Mapping[str, object]) -> Mapping[str, object]:
        try:
            if self._client is not None:
                response = await self._client.post(self.endpoint, json=dict(payload))
            else:
                async with httpx.AsyncClient(timeout=self.timeout_seconds, transport=self._transport) as client:
                    response = await client.post(self.endpoint, json=dict(payload))
        except Exception as exc:
            raise GeneratorClientError(f"vLLM request failed at {self.endpoint}") from exc

        status_code = getattr(response, "status_code", 200)
        if status_code >= 400:
            detail = getattr(response, "text", "")
            raise GeneratorClientError(f"vLLM returned HTTP {status_code}: {detail[:500]}")
        try:
            body = response.json()
        except Exception as exc:
            raise GeneratorClientError("vLLM returned invalid JSON") from exc
        if not isinstance(body, Mapping):
            raise GeneratorClientError("vLLM response must be a JSON object")
        return body


def _extract_text(body: Mapping[str, object]) -> str:
    choices = body.get("choices")
    if not isinstance(choices, list) or not choices:
        raise GeneratorClientError("vLLM response has no choices")
    first = choices[0]
    if not isinstance(first, Mapping):
        raise GeneratorClientError("vLLM response choice must be an object")
    text = first.get("text")
    if isinstance(text, str):
        return text
    message = first.get("message")
    if isinstance(message, Mapping) and isinstance(message.get("content"), str):
        return str(message["content"])
    raise GeneratorClientError("vLLM response choice has no text content")
