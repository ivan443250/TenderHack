"""Small internal inference gateway for the Cloud.ru Docker RUN image.

The gateway has a deliberately narrow surface.  It forwards only the three
known provider routes to configured loopback providers, never makes product or
business decisions, and does not log request bodies.  The implementation uses
the Python standard library so the runtime image has no model/framework
dependency beyond the pinned llama.cpp CUDA image and Python itself.
"""

from __future__ import annotations

import hmac
import json
import logging
import math
import os
import re
import secrets
import threading
import time
from dataclasses import dataclass
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Mapping
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import Request, build_opener, HTTPRedirectHandler


LOGGER = logging.getLogger("tenderhack.inference.gateway")
MAX_BODY_BYTES = 2_000_000
REQUEST_ID_RE = re.compile(r"^[A-Za-z0-9._:-]{1,128}$")
DEFAULT_EMBEDDING_MODEL = "ai-sage/Giga-Embeddings-instruct-3B-0826"
DEFAULT_GENERATOR_MODEL = "empero-ai/Qwen3.8-4B-Distill"
DEFAULT_EMBEDDING_DIMENSION = 2048


class ConfigError(ValueError):
    """Raised when an internal provider URL or runtime setting is unsafe."""


class _NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *_args: Any, **_kwargs: Any) -> None:  # pragma: no cover - defensive
        return None


_UPSTREAM_OPENER = build_opener(_NoRedirect)


@dataclass(frozen=True)
class GatewayConfig:
    embedding_url: str = "http://127.0.0.1:8081"
    generator_url: str = "http://127.0.0.1:8082"
    reranker_url: str | None = None
    reranker_enabled: bool = False
    api_token: str | None = None
    embedding_model: str = DEFAULT_EMBEDDING_MODEL
    generator_model: str = DEFAULT_GENERATOR_MODEL
    embedding_dimension: int = DEFAULT_EMBEDDING_DIMENSION
    upstream_timeout_seconds: float = 30.0
    readiness_cache_seconds: float = 2.0
    max_body_bytes: int = MAX_BODY_BYTES
    allowed_upstream_hosts: tuple[str, ...] = ("127.0.0.1", "localhost", "::1")

    def __post_init__(self) -> None:
        if self.embedding_dimension != DEFAULT_EMBEDDING_DIMENSION:
            raise ConfigError("GIGA_EMBEDDING_DIMENSION must be 2048")
        if self.upstream_timeout_seconds <= 0:
            raise ConfigError("UPSTREAM_TIMEOUT_SECONDS must be positive")
        if self.readiness_cache_seconds < 0:
            raise ConfigError("READINESS_CACHE_SECONDS cannot be negative")
        if self.max_body_bytes < 1024:
            raise ConfigError("max_body_bytes is too small")
        _validate_internal_url(self.embedding_url, self.allowed_upstream_hosts, "EMBEDDING_URL")
        _validate_internal_url(self.generator_url, self.allowed_upstream_hosts, "GENERATOR_URL")
        if self.reranker_enabled and not self.reranker_url:
            raise ConfigError("RERANKER_URL is required when RERANKER_ENABLED=true")
        if self.reranker_url:
            _validate_internal_url(self.reranker_url, self.allowed_upstream_hosts, "RERANKER_URL")

    @classmethod
    def from_env(cls) -> "GatewayConfig":
        allowed = tuple(
            host.strip().lower()
            for host in os.getenv("INTERNAL_UPSTREAM_HOSTS", "127.0.0.1,localhost,::1").split(",")
            if host.strip()
        )
        reranker_url = os.getenv("RERANKER_URL", "").strip() or None
        return cls(
            embedding_url=os.getenv("EMBEDDING_URL", "http://127.0.0.1:8081").rstrip("/"),
            generator_url=os.getenv("GENERATOR_URL", "http://127.0.0.1:8082").rstrip("/"),
            reranker_url=reranker_url.rstrip("/") if reranker_url else None,
            reranker_enabled=_env_bool("RERANKER_ENABLED", False),
            api_token=os.getenv("INFERENCE_API_TOKEN") or None,
            embedding_model=os.getenv("GIGA_MODEL_ID", DEFAULT_EMBEDDING_MODEL),
            generator_model=os.getenv("QWEN_MODEL_ID", DEFAULT_GENERATOR_MODEL),
            embedding_dimension=_env_int("GIGA_EMBEDDING_DIMENSION", DEFAULT_EMBEDDING_DIMENSION),
            upstream_timeout_seconds=_env_float("UPSTREAM_TIMEOUT_SECONDS", 30.0),
            readiness_cache_seconds=_env_float("READINESS_CACHE_SECONDS", 2.0),
            max_body_bytes=_env_int("MAX_BODY_BYTES", MAX_BODY_BYTES),
            allowed_upstream_hosts=allowed or ("127.0.0.1", "localhost", "::1"),
        )


@dataclass(frozen=True)
class UpstreamResult:
    status: int
    body: bytes
    content_type: str
    error: str | None = None


def _env_bool(name: str, default: bool) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default
    return raw.strip().lower() not in {"0", "false", "no", "off"}


def _env_int(name: str, default: int) -> int:
    raw = os.getenv(name)
    if raw is None:
        return default
    try:
        return int(raw)
    except ValueError as exc:
        raise ConfigError(f"{name} must be an integer") from exc


def _env_float(name: str, default: float) -> float:
    raw = os.getenv(name)
    if raw is None:
        return default
    try:
        return float(raw)
    except ValueError as exc:
        raise ConfigError(f"{name} must be a number") from exc


def _validate_internal_url(url: str, allowed_hosts: tuple[str, ...], setting: str) -> None:
    parsed = urlparse(url)
    host = (parsed.hostname or "").lower()
    if parsed.scheme not in {"http", "https"} or not host:
        raise ConfigError(f"{setting} must be an http(s) URL")
    if host not in {item.lower() for item in allowed_hosts}:
        raise ConfigError(f"{setting} host is not in INTERNAL_UPSTREAM_HOSTS")
    if parsed.username or parsed.password:
        raise ConfigError(f"{setting} must not contain credentials")
    if parsed.query or parsed.fragment:
        raise ConfigError(f"{setting} must not contain query or fragment")


def _open_url(request: Request, timeout: float) -> Any:
    """Open a provider request without following redirects."""

    return _UPSTREAM_OPENER.open(request, timeout=timeout)


def request_upstream(
    url: str,
    payload: bytes,
    *,
    timeout: float,
    request_id: str,
) -> UpstreamResult:
    request = Request(
        url,
        data=payload,
        method="POST",
        headers={
            "Accept": "application/json",
            "Content-Type": "application/json",
            "X-Request-ID": request_id,
        },
    )
    try:
        response = _open_url(request, timeout)
        with response:
            body = response.read(MAX_BODY_BYTES + 1)
            if len(body) > MAX_BODY_BYTES:
                return UpstreamResult(502, b"", "application/json", "upstream_response_too_large")
            content_type = response.headers.get("Content-Type", "application/json").split(";", 1)[0]
            return UpstreamResult(int(response.status), body, content_type or "application/json")
    except HTTPError as exc:
        body = exc.read(MAX_BODY_BYTES + 1)
        if len(body) > MAX_BODY_BYTES:
            body = b""
        return UpstreamResult(int(exc.code), body, "application/json", "upstream_http_error")
    except (TimeoutError, URLError, OSError) as exc:
        return UpstreamResult(503, b"", "application/json", f"upstream_unavailable:{type(exc).__name__}")
    except Exception as exc:  # pragma: no cover - hardening for unexpected transports
        LOGGER.error("upstream request failed category=%s", type(exc).__name__)
        return UpstreamResult(503, b"", "application/json", "upstream_unavailable")


def _json_bytes(value: Mapping[str, Any]) -> bytes:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def _error_payload(code: str, message: str) -> bytes:
    return _json_bytes({"error": {"code": code, "message": message}})


def _parse_json(body: bytes) -> Mapping[str, Any] | None:
    try:
        parsed = json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None
    return parsed if isinstance(parsed, Mapping) else None


def _strip_reasoning(text: str) -> str:
    """Remove balanced ``<think>`` blocks and fail closed on malformed ones."""

    result: list[str] = []
    cursor = 0
    lowered = text.lower()
    while True:
        start = lowered.find("<think>", cursor)
        end = lowered.find("</think>", cursor)
        if end >= 0 and (start < 0 or end < start):
            raise ValueError("unterminated or unmatched reasoning block")
        if start < 0:
            result.append(text[cursor:])
            break
        result.append(text[cursor:start])
        close = lowered.find("</think>", start + len("<think>"))
        if close < 0:
            raise ValueError("unterminated reasoning block")
        nested = lowered.find("<think>", start + len("<think>"), close)
        if nested >= 0:
            raise ValueError("nested reasoning block")
        cursor = close + len("</think>")
    return "".join(result).strip()


def _sanitize_chat_response(body: bytes) -> bytes:
    parsed = _parse_json(body)
    if parsed is None:
        raise ValueError("chat response is not a JSON object")
    choices = parsed.get("choices")
    if not isinstance(choices, list):
        raise ValueError("chat response has no choices")
    mutable = dict(parsed)
    mutable_choices: list[Any] = []
    for choice in choices:
        if not isinstance(choice, Mapping):
            mutable_choices.append(choice)
            continue
        mutable_choice = dict(choice)
        message = choice.get("message")
        if isinstance(message, Mapping) and isinstance(message.get("content"), str):
            mutable_message = dict(message)
            mutable_message["content"] = _strip_reasoning(str(message["content"]))
            mutable_choice["message"] = mutable_message
        elif isinstance(choice.get("text"), str):
            mutable_choice["text"] = _strip_reasoning(str(choice["text"]))
        mutable_choices.append(mutable_choice)
    mutable["choices"] = mutable_choices
    return _json_bytes(mutable)


def _embedding_ready(body: bytes, dimension: int) -> bool:
    parsed = _parse_json(body)
    if parsed is None or not isinstance(parsed.get("data"), list) or not parsed["data"]:
        return False
    first = parsed["data"][0]
    if not isinstance(first, Mapping) or not isinstance(first.get("embedding"), list):
        return False
    vector = first["embedding"]
    if len(vector) != dimension:
        return False
    try:
        values = [float(item) for item in vector]
    except (TypeError, ValueError):
        return False
    if not values or any(not math.isfinite(value) for value in values):
        return False
    norm = math.sqrt(sum(value * value for value in values))
    return math.isfinite(norm) and norm > 0


def _generator_ready(body: bytes) -> bool:
    parsed = _parse_json(body)
    if parsed is None or not isinstance(parsed.get("choices"), list) or not parsed["choices"]:
        return False
    first = parsed["choices"][0]
    if not isinstance(first, Mapping):
        return False
    message = first.get("message")
    content = message.get("content") if isinstance(message, Mapping) else first.get("text")
    if not isinstance(content, str):
        return False
    try:
        return bool(_strip_reasoning(content))
    except ValueError:
        return False


def _reranker_ready(body: bytes) -> bool:
    parsed = _parse_json(body)
    if parsed is None:
        return False
    scores = parsed.get("scores")
    if not isinstance(scores, list) or len(scores) != 1:
        return False
    try:
        return math.isfinite(float(scores[0]))
    except (TypeError, ValueError):
        return False


def readiness_snapshot(config: GatewayConfig, request_id: str = "readiness") -> dict[str, Any]:
    embedding_payload = _json_bytes({"model": config.embedding_model, "input": ["health smoke"]})
    embedding = request_upstream(
        f"{config.embedding_url}/v1/embeddings",
        embedding_payload,
        timeout=config.upstream_timeout_seconds,
        request_id=request_id,
    )
    embedding_status = "READY" if embedding.status < 400 and _embedding_ready(embedding.body, config.embedding_dimension) else "UNAVAILABLE"

    generator_payload = _json_bytes(
        {
            "model": config.generator_model,
            "messages": [{"role": "user", "content": "Return the word ready."}],
            "temperature": 0,
            "max_tokens": 8,
        }
    )
    generator = request_upstream(
        f"{config.generator_url}/v1/chat/completions",
        generator_payload,
        timeout=config.upstream_timeout_seconds,
        request_id=request_id,
    )
    generator_status = "READY" if generator.status < 400 and _generator_ready(generator.body) else "UNAVAILABLE"

    reranker_status = "DISABLED"
    if config.reranker_enabled:
        reranker_payload = _json_bytes({"query": "health smoke", "documents": ["health smoke"]})
        reranker = request_upstream(
            f"{(config.reranker_url or '').rstrip('/')}/v1/rerank",
            reranker_payload,
            timeout=config.upstream_timeout_seconds,
            request_id=request_id,
        )
        reranker_status = "READY" if reranker.status < 400 and _reranker_ready(reranker.body) else "FAILED"

    required_ready = embedding_status == "READY" and generator_status == "READY"
    profile = "FULL" if required_ready and reranker_status == "READY" else "REDUCED"
    return {
        "ready": required_ready,
        "profile": profile,
        "embedding": embedding_status,
        "generator": generator_status,
        "reranker": reranker_status,
    }


class InferenceGatewayHandler(BaseHTTPRequestHandler):
    """HTTP handler with fixed provider routes and health semantics."""

    protocol_version = "HTTP/1.1"
    server: "GatewayServer"

    def log_message(self, _format: str, *_args: Any) -> None:
        # Access logs are emitted by _log_request without request bodies.
        return None

    def do_GET(self) -> None:  # noqa: N802 - stdlib handler API
        started = time.perf_counter()
        request_id = self._request_id()
        if self.path == "/health/live":
            self._send_json(HTTPStatus.OK, {"live": True})
            self._log_request(request_id, "/health/live", 200, "health", started)
            return
        if self.path == "/health/ready":
            snapshot = self._readiness()
            status = HTTPStatus.OK if snapshot["ready"] else HTTPStatus.SERVICE_UNAVAILABLE
            self._send_json(status, snapshot)
            self._log_request(request_id, "/health/ready", int(status), "health", started)
            return
        if self.path == "/v1/capabilities":
            if not self._authorized():
                self._send_error_json(HTTPStatus.UNAUTHORIZED, "AUTH_REQUIRED", "service authentication required")
                return
            snapshot = self._readiness()
            capabilities = {
                "profile": snapshot["profile"],
                "embedding": {
                    "available": snapshot["embedding"] == "READY",
                    "model": self.server.config.embedding_model,
                    "dimension": self.server.config.embedding_dimension,
                },
                "generator": {
                    "available": snapshot["generator"] == "READY",
                    "model": self.server.config.generator_model,
                    "context_limit": 8192,
                },
                "reranker": {
                    "available": snapshot["reranker"] == "READY",
                    "status": snapshot["reranker"],
                },
                "runtime": "llama.cpp / llama-server",
                "backend": "CUDA",
            }
            self._send_json(HTTPStatus.OK, capabilities)
            self._log_request(request_id, "/v1/capabilities", 200, "capabilities", started)
            return
        self._send_error_json(HTTPStatus.NOT_FOUND, "NOT_FOUND", "route not found")

    def do_POST(self) -> None:  # noqa: N802 - stdlib handler API
        started = time.perf_counter()
        request_id = self._request_id()
        route = self.path.split("?", 1)[0]
        if not self._authorized():
            self._send_error_json(HTTPStatus.UNAUTHORIZED, "AUTH_REQUIRED", "service authentication required")
            self._log_request(request_id, route, 401, "auth", started)
            return
        if route not in {"/v1/embeddings", "/v1/chat/completions", "/v1/rerank"}:
            self._send_error_json(HTTPStatus.NOT_FOUND, "NOT_FOUND", "route not found")
            return
        if route == "/v1/rerank" and not self.server.config.reranker_enabled:
            self._send_error_json(HTTPStatus.SERVICE_UNAVAILABLE, "MODEL_UNAVAILABLE", "reranker is disabled")
            self._log_request(request_id, route, 503, "reranker", started)
            return
        body = self._read_body()
        if body is None:
            self._send_error_json(HTTPStatus.BAD_REQUEST, "INVALID_REQUEST", "request body is missing or too large")
            self._log_request(request_id, route, 400, route[4:], started)
            return
        parsed = _parse_json(body)
        if parsed is None:
            self._send_error_json(HTTPStatus.BAD_REQUEST, "INVALID_JSON", "request must be a JSON object")
            self._log_request(request_id, route, 400, route[4:], started)
            return
        if route == "/v1/chat/completions" and parsed.get("stream") is True:
            self._send_error_json(HTTPStatus.BAD_REQUEST, "STREAMING_UNSUPPORTED", "non-streaming chat is required")
            self._log_request(request_id, route, 400, "generator", started)
            return

        provider, base_url, upstream_path = self._provider_target(route)
        result = request_upstream(
            f"{base_url}{upstream_path}",
            body,
            timeout=self.server.config.upstream_timeout_seconds,
            request_id=request_id,
        )
        response_body = result.body
        response_type = result.content_type
        status = result.status
        if route == "/v1/chat/completions" and status < 400:
            try:
                response_body = _sanitize_chat_response(response_body)
                response_type = "application/json"
            except ValueError:
                status = HTTPStatus.BAD_GATEWAY
                response_body = _error_payload("MODEL_ERROR", "generator response failed safety validation")
                response_type = "application/json"
        if result.error and status >= 500:
            response_body = _error_payload("MODEL_UNAVAILABLE", "configured provider is unavailable")
            response_type = "application/json"
        self._send_bytes(status, response_body, response_type)
        self._log_request(request_id, route, int(status), provider, started)

    def _provider_target(self, route: str) -> tuple[str, str, str]:
        if route == "/v1/embeddings":
            return "embedding", self.server.config.embedding_url, "/v1/embeddings"
        if route == "/v1/chat/completions":
            return "generator", self.server.config.generator_url, "/v1/chat/completions"
        return "reranker", self.server.config.reranker_url or "", "/v1/rerank"

    def _readiness(self) -> dict[str, Any]:
        now = time.monotonic()
        with self.server.readiness_lock:
            if self.server.readiness_value and now - self.server.readiness_at < self.server.config.readiness_cache_seconds:
                return dict(self.server.readiness_value)
        snapshot = readiness_snapshot(self.server.config, self._request_id())
        with self.server.readiness_lock:
            self.server.readiness_value = dict(snapshot)
            self.server.readiness_at = now
        return snapshot

    def _authorized(self) -> bool:
        token = self.server.config.api_token
        if not token:
            return True
        supplied = self.headers.get("Authorization", "")
        expected = f"Bearer {token}"
        return hmac.compare_digest(supplied, expected)

    def _request_id(self) -> str:
        supplied = self.headers.get("X-Request-ID", "")
        return supplied if REQUEST_ID_RE.fullmatch(supplied) else secrets.token_hex(8)

    def _read_body(self) -> bytes | None:
        try:
            length = int(self.headers.get("Content-Length", "-1"))
        except ValueError:
            return None
        if length < 0 or length > self.server.config.max_body_bytes:
            return None
        return self.rfile.read(length)

    def _send_json(self, status: int | HTTPStatus, payload: Mapping[str, Any]) -> None:
        self._send_bytes(int(status), _json_bytes(payload), "application/json")

    def _send_error_json(self, status: int | HTTPStatus, code: str, message: str) -> None:
        self._send_bytes(int(status), _error_payload(code, message), "application/json")

    def _send_bytes(self, status: int, body: bytes, content_type: str) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _log_request(self, request_id: str, route: str, status: int, provider: str, started: float) -> None:
        elapsed_ms = (time.perf_counter() - started) * 1000
        LOGGER.info(
            "request_id=%s route=%s status=%d provider=%s latency_ms=%.1f",
            request_id,
            route,
            status,
            provider,
            elapsed_ms,
        )


class GatewayServer(ThreadingHTTPServer):
    allow_reuse_address = True
    daemon_threads = True

    def __init__(self, address: tuple[str, int], config: GatewayConfig):
        super().__init__(address, InferenceGatewayHandler)
        self.config = config
        self.readiness_lock = threading.Lock()
        self.readiness_value: dict[str, Any] = {}
        self.readiness_at = 0.0


def create_server(config: GatewayConfig | None = None, *, host: str | None = None, port: int | None = None) -> GatewayServer:
    config = config or GatewayConfig.from_env()
    bind_host = host or os.getenv("GATEWAY_HOST", "0.0.0.0")
    bind_port = port if port is not None else _env_int("GATEWAY_PORT", 8080)
    if not 0 <= bind_port <= 65535:
        raise ConfigError("GATEWAY_PORT must be between 0 and 65535")
    return GatewayServer((bind_host, bind_port), config)


def main() -> int:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s %(message)s",
    )
    server = create_server()
    LOGGER.info("gateway listening host=%s port=%d", server.server_address[0], server.server_address[1])
    try:
        server.serve_forever(poll_interval=0.5)
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":  # pragma: no cover - exercised by container startup
    raise SystemExit(main())
