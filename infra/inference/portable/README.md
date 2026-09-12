# Local portable NVIDIA runtime

This profile is for the Windows + Docker Desktop + NVIDIA RTX 3060 Laptop
host. It is separate from `cloudru/` and does not change the A100 image.

The image uses the official `llama.cpp` CUDA 12.8 runtime
`server-cuda12-b9426` pinned by digest. It reuses the existing gateway,
entrypoint and verification scripts, and the same Giga/Qwen GGUF artifacts as
the bundled profile. Querit stays disabled.

Defaults are intentionally conservative for a 6 GiB laptop GPU:

- `GENERATOR_PARALLEL=1`
- `EMBEDDING_BATCH=2048` (required by this Giga model/runtime during slot
  initialization; the request path remains single-request)
- `GENERATOR_CONTEXT=4096`
- `LLAMA_GPU_LAYERS=1` (override only after measuring a safe value)

The portable profile adds `--ctx-size 2048 --parallel 1 --no-warmup` to the
Giga server and disables Qwen reasoning with `--reasoning off`. The latter
keeps the gateway response in the frozen content-only contract and prevents
internal reasoning from being returned as a response field.

Build locally from the repository root (weights are read from the ignored
`infra/inference/bundled/models/` directory):

```powershell
$sha = git rev-parse --short HEAD
docker build --platform linux/amd64 `
  -f infra/inference/portable/Dockerfile `
  -t tenderhack-inference-portable:$sha `
  infra/inference
```

Run on the RTX host:

```powershell
docker run --rm --gpus all --publish 8080:8080 `
  --name tenderhack-inference-portable `
  tenderhack-inference-portable:$sha
```

The gateway exposes the same `/health/live`, `/health/ready`,
`/v1/capabilities`, `/v1/embeddings` and `/v1/chat/completions` contracts.
Models are baked into this local image, never downloaded at startup, and are
not tracked by Git. This profile is not an A100 performance benchmark.
