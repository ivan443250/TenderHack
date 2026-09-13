"""Extractive test double for the OpenAI-compatible generator endpoint.

Not a runtime policy and not a model: it echoes verbatim substrings of the
evidence it is given, so ``verify_v1`` (deterministic claim verification)
passes for real, letting the rest of the demo/E2E stack (draft -> verify ->
ANSWER with sources) be exercised without RunPod/GPU. See ``README.md`` in
this directory and ``docs/plans/active/2026-09-demo-readiness.md`` (A1).

Deliberately dependency-free beyond fastapi/uvicorn: it must not require the
``tenderhack_knowledge`` package to be installed, and it must stay simple
enough to audit at a glance for the "test double, not policy" property.
"""

from __future__ import annotations

import json
import re
from typing import Any

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

MODEL_NAME = "stub-extractive-v0"
INPUT_MARKER = "INPUT DATA:\n"
MAX_CLAIMS = 3
_SENTENCE_END_RE = re.compile(r"[.!?](?=\s|$)")

app = FastAPI(title="generator-stub")


def _select_claim_span(text: str) -> str:
    """Return a verbatim prefix of ``text`` covering its first 1-2 sentences.

    Slicing (rather than re-joining split parts) guarantees the result stays
    an exact substring of the source fragment, which is what lets the
    deterministic verifier confirm ``evidence_quote`` without ambiguity.
    """

    stripped = text.strip()
    if not stripped:
        return stripped
    matches = list(_SENTENCE_END_RE.finditer(stripped))
    if not matches:
        return stripped[:400]
    end = matches[0].end()
    if len(matches) > 1 and end < 200:
        end = matches[1].end()
    return stripped[:end].strip()


def _build_draft(evidence: list[dict[str, Any]]) -> dict[str, Any]:
    claims = []
    lines = []
    for index, item in enumerate(evidence[:MAX_CLAIMS], start=1):
        fragment_id = item.get("fragment_id")
        text = item.get("text") or ""
        page = item.get("page")
        span = _select_claim_span(str(text))
        if not fragment_id or not span:
            continue
        claim_id = f"c{index}"
        claims.append(
            {
                "claim_id": claim_id,
                "text": span,
                "fragment_ids": [fragment_id],
                "evidence_quote": span,
            }
        )
        lines.append(f"- {span} (стр. {page})" if page is not None else f"- {span}")
    return {"draft_markdown": "\n".join(lines), "claims": claims}


def _extract_input_data(messages: list[dict[str, Any]]) -> dict[str, Any] | None:
    user_content: str | None = None
    for message in reversed(messages):
        if message.get("role") == "user" and isinstance(message.get("content"), str):
            user_content = message["content"]
            break
    if user_content is None:
        return None
    marker_at = user_content.find(INPUT_MARKER)
    if marker_at == -1:
        return None
    raw_json = user_content[marker_at + len(INPUT_MARKER) :]
    try:
        parsed = json.loads(raw_json)
    except json.JSONDecodeError:
        return None
    if not isinstance(parsed, dict):
        return None
    return parsed


@app.post("/v1/chat/completions")
async def chat_completions(request: Request) -> JSONResponse:
    body = await request.json()
    messages = body.get("messages") if isinstance(body, dict) else None
    if not isinstance(messages, list):
        return JSONResponse(status_code=400, content={"error": "messages array is required"})
    input_data = _extract_input_data(messages)
    if input_data is None:
        return JSONResponse(status_code=400, content={"error": "INPUT DATA marker not found in prompt"})
    evidence = input_data.get("evidence")
    draft = _build_draft(evidence if isinstance(evidence, list) else [])
    content = json.dumps(draft, ensure_ascii=False)
    return JSONResponse(
        content={
            "id": "stub",
            "model": MODEL_NAME,
            "choices": [
                {
                    "index": 0,
                    "message": {"role": "assistant", "content": content},
                    "finish_reason": "stop",
                }
            ],
            "usage": {"prompt_tokens": 0, "completion_tokens": 0, "total_tokens": 0},
        }
    )


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok", "model": MODEL_NAME}
