# Self-contained portable GPU image

This is a separate bundled profile. It keeps the existing `cloudru/` slim
profile unchanged and bakes the two pinned GGUF files into the image:

- Giga embeddings `ai-babai/giga-embeddings-0826-3b-gguf` at revision
  `04c5a2d751ce20908200de2324444a14a81b1d80`;
- Qwen generator `empero-ai/Qwen3.8-4B-Distill-GGUF` at revision
  `391fc7d103e3942a408def3e4f51c2f85d464417`.

The fetch helper verifies Giga's frozen SHA256 and records the measured Qwen
SHA256 in `model-manifest.json`. It never downloads Querit and the container
never downloads weights at startup. GGUF files are ignored by Git.

## Fetch the two weights (PowerShell)

Run from the repository root after checking local disk space:

```powershell
powershell -ExecutionPolicy Bypass -File infra/inference/bundled/scripts/fetch-models.ps1
```

The script requires at least 20 GB free on the destination drive and uses
immutable Hugging Face revision URLs with retry/resume. A Giga hash mismatch is
fatal. The generated manifest is the build-time source of truth for both
bundled files.

## Build and inspect locally

The context is `infra/inference` so the already-reviewed gateway and entrypoint
are reused; no application source is included. The tag below is based on the
source HEAD used for the build:

```powershell
$sha = git rev-parse --short HEAD
docker buildx build `
  --platform linux/amd64 `
  -f infra/inference/bundled/Dockerfile `
  -t tenderhack-inference-bundled:$sha `
  --load `
  infra/inference
```

The image exposes only gateway port `8080`; Giga `8081` and Qwen `8082` bind to
loopback. Runtime remains UID/GID `1000:1000` and keeps the `/app` CUDA library
path fix. Runtime knobs include `LLAMA_GPU_LAYERS`, `GENERATOR_PARALLEL`,
`GENERATOR_CONTEXT`, `EMBEDDING_BATCH` and `STARTUP_TIMEOUT_SECONDS`.

Static inspection without starting CUDA inference:

```powershell
docker run --rm --entrypoint /bin/sh tenderhack-inference-bundled:$sha -c `
  "id; ls -lh /models; sha256sum /models/*.gguf; /app/llama-server --version"
```

On a GPU host, the generic run shape is:

```bash
docker run --rm --gpus all --publish 8080:8080 \
  --name tenderhack-inference \
  tenderhack-inference-bundled:<git-sha>
```

PowerShell equivalent:

```powershell
docker run --rm --gpus all --publish 8080:8080 `
  --name tenderhack-inference `
  tenderhack-inference-bundled:$sha
```

Cloud.ru is only one possible target: use an A100 80 GB Docker RUN instance,
port `8080`, no model volume and no startup download. A registry tag template
is `tenderhack.cr.cloud.ru/tenderhack-inference-bundled:<git-sha>`; push only
after local and A100 validation. No external LLM/search API, Object Storage,
or Cloud.ru service is required by this image. Querit remains disabled and
`UNVERIFIED`.
