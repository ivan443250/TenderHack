from __future__ import annotations

import io
import json
import logging
import threading
import unittest
from http.client import HTTPConnection
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from app import GatewayConfig, create_server


class FakeProviderHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, _format: str, *_args: object) -> None:
        return None

    def do_POST(self) -> None:  # noqa: N802 - stdlib handler API
        length = int(self.headers.get("Content-Length", "0"))
        request = json.loads(self.rfile.read(length))
        if self.path == "/v1/embeddings":
            body = {"data": [{"index": 0, "embedding": [1.0] + [0.0] * 2047}]}
        elif self.path == "/v1/chat/completions":
            content = str(request.get("test_content", "<think>hidden</think>ready"))
            body = {"choices": [{"message": {"content": content}}]}
        elif self.path == "/v1/rerank":
            body = {"scores": [0.5]}
        else:
            self.send_response(404)
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        encoded = json.dumps(body).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)


class GatewayTests(unittest.TestCase):
    def setUp(self) -> None:
        self.provider = ThreadingHTTPServer(("127.0.0.1", 0), FakeProviderHandler)
        self.provider_thread = threading.Thread(target=self.provider.serve_forever, daemon=True)
        self.provider_thread.start()
        provider_port = self.provider.server_address[1]
        self.config = GatewayConfig(
            embedding_url=f"http://127.0.0.1:{provider_port}",
            generator_url=f"http://127.0.0.1:{provider_port}",
            upstream_timeout_seconds=0.5,
            readiness_cache_seconds=0,
        )
        self.gateway = create_server(self.config, host="127.0.0.1", port=0)
        self.gateway_thread = threading.Thread(target=self.gateway.serve_forever, daemon=True)
        self.gateway_thread.start()
        self.port = self.gateway.server_address[1]

    def tearDown(self) -> None:
        self.gateway.shutdown()
        self.gateway.server_close()
        self.provider.shutdown()
        self.provider.server_close()

    def request(self, method: str, path: str, body: object | None = None, headers: dict[str, str] | None = None):
        connection = HTTPConnection("127.0.0.1", self.port, timeout=2)
        encoded = None if body is None else json.dumps(body).encode()
        request_headers = {"Content-Type": "application/json"}
        if encoded is not None:
            request_headers["Content-Length"] = str(len(encoded))
        request_headers.update(headers or {})
        connection.request(method, path, body=encoded, headers=request_headers)
        response = connection.getresponse()
        result = response.status, response.read()
        connection.close()
        return result

    def test_liveness_and_readiness_with_fake_providers(self) -> None:
        status, body = self.request("GET", "/ping")
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(body), {"status": "ok"})

        status, body = self.request("GET", "/health/live")
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(body), {"live": True})

        status, body = self.request("GET", "/health/ready")
        self.assertEqual(status, 200)
        value = json.loads(body)
        self.assertEqual(value["profile"], "REDUCED")
        self.assertTrue(value["ready"])
        self.assertEqual(value["reranker"], "DISABLED")

    def test_readiness_is_false_when_required_providers_are_unavailable(self) -> None:
        unavailable = GatewayConfig(
            embedding_url="http://127.0.0.1:1",
            generator_url="http://127.0.0.1:1",
            upstream_timeout_seconds=0.1,
            readiness_cache_seconds=0,
        )
        server = create_server(unavailable, host="127.0.0.1", port=0)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        port = server.server_address[1]
        connection = HTTPConnection("127.0.0.1", port, timeout=2)
        connection.request("GET", "/health/ready")
        response = connection.getresponse()
        self.assertEqual(response.status, 503)
        value = json.loads(response.read())
        self.assertFalse(value["ready"])
        self.assertEqual(value["embedding"], "UNAVAILABLE")
        self.assertEqual(value["generator"], "UNAVAILABLE")
        connection.close()
        server.shutdown()
        server.server_close()

    def test_embedding_proxy_and_reranker_disabled(self) -> None:
        status, body = self.request("POST", "/v1/embeddings", {"input": ["secret user text"]})
        self.assertEqual(status, 200)
        self.assertEqual(len(json.loads(body)["data"][0]["embedding"]), 2048)

        status, body = self.request("POST", "/v1/rerank", {"query": "q", "documents": ["d"]})
        self.assertEqual(status, 503)
        self.assertEqual(json.loads(body)["error"]["code"], "MODEL_UNAVAILABLE")

    def test_chat_proxy_strips_reasoning_and_fails_closed(self) -> None:
        status, body = self.request("POST", "/v1/chat/completions", {"test_content": "<think>hidden</think>visible"})
        self.assertEqual(status, 200)
        content = json.loads(body)["choices"][0]["message"]["content"]
        self.assertEqual(content, "visible")

        status, body = self.request("POST", "/v1/chat/completions", {"test_content": "<think>unterminated"})
        self.assertEqual(status, 502)
        self.assertEqual(json.loads(body)["error"]["code"], "MODEL_ERROR")

    def test_authentication_is_optional_or_required(self) -> None:
        token_config = GatewayConfig(
            embedding_url=self.config.embedding_url,
            generator_url=self.config.generator_url,
            api_token="secret-token",
            upstream_timeout_seconds=0.5,
            readiness_cache_seconds=0,
        )
        authenticated = create_server(token_config, host="127.0.0.1", port=0)
        thread = threading.Thread(target=authenticated.serve_forever, daemon=True)
        thread.start()
        port = authenticated.server_address[1]
        connection = HTTPConnection("127.0.0.1", port, timeout=2)
        connection.request("GET", "/ping")
        response = connection.getresponse()
        self.assertEqual(response.status, 200)
        response.read()
        connection.close()
        connection = HTTPConnection("127.0.0.1", port, timeout=2)
        connection.request("GET", "/v1/capabilities")
        response = connection.getresponse()
        self.assertEqual(response.status, 401)
        response.read()
        connection.close()
        connection = HTTPConnection("127.0.0.1", port, timeout=2)
        connection.request("GET", "/v1/capabilities", headers={"Authorization": "Bearer secret-token"})
        response = connection.getresponse()
        self.assertEqual(response.status, 200)
        response.read()
        connection.close()
        authenticated.shutdown()
        authenticated.server_close()

    def test_sensitive_content_is_not_logged(self) -> None:
        stream = io.StringIO()
        handler = logging.StreamHandler(stream)
        logger = logging.getLogger("tenderhack.inference.gateway")
        logger.addHandler(handler)
        try:
            self.request("POST", "/v1/embeddings", {"input": ["TOP-SECRET-REQUEST"]})
        finally:
            logger.removeHandler(handler)
        self.assertNotIn("TOP-SECRET-REQUEST", stream.getvalue())


if __name__ == "__main__":
    unittest.main()
