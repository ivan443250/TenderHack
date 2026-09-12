"""Opt-in capability smoke for the selected local inference stack.

Run explicitly with ``python -m tenderhack_knowledge.inference.smoke`` (or the
project script).  Ordinary imports and tests never execute this module and it
does not download model weights unless ``--allow-model-download`` is supplied.
"""

from __future__ import annotations

import argparse
import asyncio
import importlib.metadata
import json
import math
import shutil
import subprocess
import time
from pathlib import Path
from typing import Any

from .config import InferenceSettings
from .embedding import Qwen3EmbeddingAdapter
from .errors import InferenceAdapterError
from .generator import VllmGeneratorClient
from .reranker import BgeRerankerAdapter


def _default_report_path() -> Path:
    return Path(__file__).resolve().parents[3] / "benchmarks" / "k0-inference-capability.json"


def _version(name: str) -> str | None:
    try:
        return importlib.metadata.version(name)
    except importlib.metadata.PackageNotFoundError:
        return None


def _nvidia_memory() -> dict[str, int] | None:
    executable = shutil.which("nvidia-smi")
    if executable is None:
        return None
    try:
        result = subprocess.run(
            [executable, "--query-gpu=memory.total,memory.free,memory.used", "--format=csv,noheader,nounits"],
            capture_output=True,
            text=True,
            timeout=5,
            check=True,
        )
        values = [int(part.strip()) for part in result.stdout.splitlines()[0].split(",")]
        if len(values) != 3:
            return None
        return {"total_mib": values[0], "free_mib": values[1], "used_mib": values[2]}
    except (OSError, subprocess.SubprocessError, IndexError, ValueError):
        return None


def _model_report(adapter: object, *, status: str, samples: dict[str, int], **extra: object) -> dict[str, object]:
    metadata = getattr(adapter, "metadata")
    return {
        "model_id": metadata["model_id"],
        "revision": metadata["revision"],
        "device": metadata["device"],
        "dtype": metadata["dtype"],
        "status": status,
        "actual_samples": samples,
        "load_time_ms": extra.pop("load_time_ms", None),
        "latency_ms": extra.pop("latency_ms", {}),
        "memory_observations": extra.pop("memory_observations", {"before": None, "after": None}),
        "schema_failures": extra.pop("schema_failures", []),
        **extra,
    }


async def _run_embedding(settings: InferenceSettings, allow_download: bool) -> dict[str, object]:
    adapter = Qwen3EmbeddingAdapter(
        model_id=settings.embedding_model_id,
        revision=settings.embedding_revision,
        device=settings.embedding_device,
        dtype=settings.embedding_dtype,
        batch_size=settings.embedding_batch_size,
    )
    samples = {"single_query": 1, "batch": 8, "long_support_request": 1}
    if not allow_download:
        return _model_report(
            adapter,
            status="BLOCKED_OPT_IN_REQUIRED",
            samples={key: 0 for key in samples},
            limitations=["Pass --allow-model-download to load local weights."],
        )
    before = _nvidia_memory()
    failures: list[str] = []
    latencies: dict[str, float | None] = {}
    actual_samples = {key: 0 for key in samples}
    load_time: float | None = None
    try:
        started = time.perf_counter()
        await asyncio.to_thread(adapter.load)
        load_time = round((time.perf_counter() - started) * 1000, 3)
        inputs = {
            "single_query": ["Как подать заявку на участие в тендере?"],
            "batch": [f"Какие документы нужны для заявки номер {index}?" for index in range(8)],
            "long_support_request": [
                "Подскажите, пожалуйста, какие сроки, условия допуска и подтверждающие документы нужны "
                "для участия в закупке, если часть сведений уже была указана в предыдущем обращении."
            ],
        }
        for name, values in inputs.items():
            started = time.perf_counter()
            output = await adapter.embed(values)
            latencies[name] = round((time.perf_counter() - started) * 1000, 3)
            actual_samples[name] = len(values)
            if len(output) != len(values) or any(len(row) != 1024 for row in output):
                failures.append(f"{name}: output shape is not (n, 1024)")
            if any(not math.isfinite(value) for row in output for value in row):
                failures.append(f"{name}: non-finite output")
    except Exception as exc:
        failures.append(f"{type(exc).__name__}: {exc}")
    after = _nvidia_memory()
    return _model_report(
        adapter,
        status="PASS" if not failures else "FAIL",
        samples=actual_samples,
        load_time_ms=load_time,
        latency_ms=latencies,
        memory_observations={"before": before, "after": after},
        schema_failures=failures,
    )


async def _run_reranker(settings: InferenceSettings, allow_download: bool) -> dict[str, object]:
    adapter = BgeRerankerAdapter(
        model_id=settings.reranker_model_id,
        revision=settings.reranker_revision,
        device=settings.reranker_device,
        dtype=settings.reranker_dtype,
        batch_size=settings.reranker_batch_size,
        max_length=settings.reranker_max_length,
    )
    samples = {"sanity_query": 1, "candidates": 2}
    if not allow_download:
        return _model_report(
            adapter,
            status="BLOCKED_OPT_IN_REQUIRED",
            samples={key: 0 for key in samples},
            limitations=["Pass --allow-model-download to load local weights."],
        )
    before = _nvidia_memory()
    failures: list[str] = []
    latencies: dict[str, float | None] = {"top_5": None, "top_20": None}
    actual_samples = {"sanity_query": 0, "candidates": 0, "top_20_candidates": 0}
    load_time: float | None = None
    query = "Какой срок подачи заявки на участие в закупке?"
    relevant = "Срок подачи заявок указан в извещении о закупке и завершается в установленную дату."
    unrelated = "Рецепт овощного супа не относится к процедуре закупки."
    try:
        started = time.perf_counter()
        await asyncio.to_thread(adapter.load)
        load_time = round((time.perf_counter() - started) * 1000, 3)
        started = time.perf_counter()
        scores = await adapter.score(query, [relevant, unrelated])
        latencies["top_5"] = round((time.perf_counter() - started) * 1000, 3)
        actual_samples["sanity_query"] = 1
        actual_samples["candidates"] = 2
        if len(scores) != 2:
            failures.append(f"sanity: returned {len(scores)} scores for 2 candidates")
        elif scores[0] <= scores[1]:
            failures.append("sanity: relevant candidate did not outrank unrelated candidate")
        started = time.perf_counter()
        await adapter.score(query, [relevant] * 20)
        latencies["top_20"] = round((time.perf_counter() - started) * 1000, 3)
        actual_samples["top_20_candidates"] = 20
    except Exception as exc:
        failures.append(f"{type(exc).__name__}: {exc}")
    after = _nvidia_memory()
    return _model_report(
        adapter,
        status="PASS" if not failures else "FAIL",
        samples=actual_samples,
        load_time_ms=load_time,
        latency_ms=latencies,
        memory_observations={"before": before, "after": after},
        schema_failures=failures,
        limitations=["Ranking sanity is an invariant check, not a quality benchmark."],
    )


async def _run_generator(settings: InferenceSettings, probe_vllm: bool) -> dict[str, object]:
    client = VllmGeneratorClient(
        base_url=settings.vllm_base_url,
        model_id=settings.generator_model_id,
        revision=settings.generator_revision,
        timeout_seconds=settings.generator_timeout_seconds,
        temperature=settings.generator_temperature,
        max_tokens=settings.generator_max_tokens,
    )
    report: dict[str, object] = {
        "model_id": settings.generator_model_id,
        "revision": settings.generator_revision,
        "runtime": "vLLM OpenAI-compatible HTTP",
        "device": "remote-local-runtime",
        "dtype": "runtime-defined",
        "status": "BLOCKED_ENVIRONMENT",
        "actual_samples": {"short_prompt": 0, "long_evidence_prompt": 0, "structured_output": 0},
        "load_time_ms": None,
        "latency_ms": {"short_prompt": None, "long_evidence_prompt": None},
        "memory_observations": {"before": None, "after": None},
        "schema_failures": [],
        "vllm_status": "blocked_environment",
        "limitations": [],
    }
    if not probe_vllm:
        report["limitations"] = [
            "K0A classified vLLM as target-Linux-only; pass --probe-vllm on a reachable local runtime to execute HTTP smoke."
        ]
        return report
    report["vllm_status"] = "probed"
    failures: list[str] = []
    prompts = {
        "short_prompt": "Кратко перечисли два шага проверки заявки.",
        "long_evidence_prompt": "На основании контекста: срок указан в извещении; документы проверены. "
        "Сформулируй краткий ответ на русском языке без добавления новых фактов.",
    }
    latencies: dict[str, float | None] = {name: None for name in prompts}
    for name, prompt in prompts.items():
        started = time.perf_counter()
        try:
            text = await client.draft(prompt)
            latencies[name] = round((time.perf_counter() - started) * 1000, 3)
            if not text.strip():
                failures.append(f"{name}: empty response")
            else:
                report["actual_samples"][name] = 1  # type: ignore[index]
        except Exception as exc:
            failures.append(f"{name}: {type(exc).__name__}: {exc}")
    try:
        structured = await client.draft(
            "Верни JSON-объект с полем status со значением ok.",
            response_format={"type": "json_object"},
        )
        report["actual_samples"]["structured_output"] = 1  # type: ignore[index]
        if not structured.strip():
            failures.append("structured_output: empty response")
    except Exception as exc:
        failures.append(f"structured_output: {type(exc).__name__}: {exc}")
    report["latency_ms"] = latencies
    report["schema_failures"] = failures
    report["status"] = "PASS" if not failures else "FAIL"
    return report


async def run_smoke(*, allow_model_download: bool = False, probe_vllm: bool = False) -> dict[str, object]:
    settings = InferenceSettings.from_env()
    embedding = await _run_embedding(settings, allow_model_download)
    reranker = await _run_reranker(settings, allow_model_download)
    generator = await _run_generator(settings, probe_vllm)
    statuses = [embedding["status"], reranker["status"], generator["status"]]
    if any(status == "FAIL" for status in statuses):
        status = "FAIL"
    elif all(status == "PASS" for status in statuses):
        status = "PASS"
    else:
        # A K0A-proven runtime blocker is an explicit limitation, not an
        # adapter assertion failure.  Per-model statuses and zero samples
        # remain visible below; this verdict keeps the gate truthful.
        status = "PASS_WITH_LIMITATIONS"
    return {
        "status": status,
        "captured_on": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "command": "tenderhack-knowledge-inference-smoke",
        "heavyweight_execution": allow_model_download or probe_vllm,
        "models": {
            "embedding": embedding,
            "reranker": reranker,
            "generator": generator,
        },
        "library_versions": {
            "python": f"{__import__('sys').version_info.major}.{__import__('sys').version_info.minor}.{__import__('sys').version_info.micro}",
            "torch": _version("torch"),
            "transformers": _version("transformers"),
            "sentence-transformers": _version("sentence-transformers"),
            "vllm": _version("vllm"),
            "httpx": _version("httpx"),
        },
        "vllm_status": generator["vllm_status"],
        "limitations": [
            "This smoke checks adapter capability and transport/schema invariants only; it does not measure retrieval or answer quality.",
            "No cloud LLM or external search API is used.",
        ],
    }


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Explicit heavyweight TenderHack inference capability smoke")
    parser.add_argument(
        "--allow-model-download",
        action="store_true",
        help="allow loading/downloading the configured embedding and reranker weights",
    )
    parser.add_argument(
        "--probe-vllm",
        action="store_true",
        help="probe the configured local vLLM HTTP endpoint",
    )
    parser.add_argument("--report", type=Path, default=_default_report_path(), help="machine-readable report path")
    return parser.parse_args()


def main() -> None:
    args = _parse_args()
    print("TenderHack inference capability smoke (explicit opt-in; may use heavyweight local runtimes)")
    report = asyncio.run(run_smoke(allow_model_download=args.allow_model_download, probe_vllm=args.probe_vllm))
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
