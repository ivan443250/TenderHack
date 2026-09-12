# Cloud-controlled inference

The Cloud.ru package in `cloudru/` is a separate Docker RUN deployment for the
controlled inference layer. It does not move the TenderHack web, .NET API or
PostgreSQL into this image.

## What the image contains

- A pinned official CUDA `llama-server` runtime;
- a small Python standard-library gateway on port `8080`;
- startup model verification, supervision and health/readiness probes;
- persistent Giga embedding and Qwen generator processes on loopback.

The image does **not** contain GGUF weights, model caches, credentials, a
database, Docker-in-Docker, systemd, or an external LLM/search client. Weights
are mounted from outside the image at `/models:ro`.

The default profile is `REDUCED`: Giga + Qwen are required and Querit is
disabled. Querit has no certified artifact/provider in this package and must
not be enabled by guessing a filename or runtime interface.

## Runtime pin

The Dockerfile pins the official CUDA 13 server image
`ghcr.io/ggml-org/llama.cpp:server-cuda13-b10902` by manifest digest. Re-check
the digest and run `/app/llama-server --version` before a production release;
the image build must not silently switch to a floating `latest` tag. Runtime
flags are checked against the pinned binary's `--help` output at startup.

## Model contract

| Provider | Local file | Internal address | Startup rule |
|---|---|---|---|
| Embedding | `/models/giga-embeddings-0826-3b-q8_0.gguf` | `127.0.0.1:8081` | exact SHA256 required; 2048-vector readiness smoke |
| Generator | `/models/Qwen3.8-4B-Q6_K.gguf` | `127.0.0.1:8082` | SHA256 recorded (no frozen expected hash); chat smoke |
| Querit | none in this package | optional configured provider | disabled unless a certified provider is supplied |

The entrypoint never downloads a model. It fails before serving if a required
file is missing or the Giga hash is wrong. Qwen's measured hash is printed for
the deployment report but is not turned into a fabricated expected value.

## Gateway surface

The gateway exposes only:

- `GET /health/live` — gateway process liveness; it does not imply model readiness;
- `GET /health/ready` — bounded smoke of every required provider;
- `GET /v1/capabilities` — technical capabilities (auth-protected when a token is configured);
- `POST /v1/embeddings` — fixed proxy to the Giga server;
- `POST /v1/chat/completions` — fixed non-streaming proxy to Qwen;
- `POST /v1/rerank` — returns `MODEL_UNAVAILABLE` while Querit is disabled.

The gateway accepts only configured internal provider hosts (loopback by
default), follows no redirects, and has no arbitrary URL proxy. If
`INFERENCE_API_TOKEN` is non-empty, all `/v1/*` requests require
`Authorization: Bearer <token>`; health probes remain available for the
platform. Request bodies, evidence and authorization headers are never logged.
Chat responses are sanitized for `<think>` blocks and fail closed on malformed
reasoning. The browser must call the .NET API, never this gateway directly.

## Build locally (static/no push)

Run from the repository root in Windows PowerShell. The build context is the
contained `infra/inference/cloudru` directory, so no application source or
weights are copied:

```powershell
$sha = git rev-parse --short HEAD
docker buildx build `
  --platform linux/amd64 `
  -f infra/inference/cloudru/Dockerfile `
  -t tenderhack-inference:$sha `
  infra/inference/cloudru
```

The local validation on a non-A100 workstation may be skipped when pulling the
CUDA base would be unnecessarily large; record `BUILD_NOT_EXECUTED_LOCALLY`
and the reason in the deployment report. Never add `--push` to this local
command. The resulting image has no `VOLUME` instruction and does not contain
model files.

## Cloud.ru Artifact Registry

Use placeholders only; never place a real key in a file or shell history:

```powershell
docker login <CLOUDRU_REGISTRY> --username <KEY_ID> --password-stdin
$sha = git rev-parse --short HEAD
docker buildx build `
  --platform linux/amd64 `
  -f infra/inference/cloudru/Dockerfile `
  -t <CLOUDRU_REGISTRY>/tenderhack-inference:$sha `
  --push `
  infra/inference/cloudru
```

`docker login` and the push are deliberately not executed by this change.
Tagging by the immutable Git SHA makes the Cloud RUN deployment auditable.

## Cloud.ru Docker RUN checklist

1. Provision a Linux `linux/amd64` Docker RUN instance with an NVIDIA A100;
   install the compatible NVIDIA container runtime.
2. Put the two verified model files in a persistent host directory outside Git.
3. In the Cloud RUN configuration use:

   ```text
   Image:          <CLOUDRU_REGISTRY>/tenderhack-inference:<git-sha>
   Architecture:  linux/amd64
   GPU:            A100
   Port:           8080
   Models:         /models (read-only)
   Minimum:        1 replica for demo
   Scale-to-zero:  OFF for demo
   Authentication: ON (INFERENCE_API_TOKEN)
   Raw logging:    OFF
   Health:         /health/live and /health/ready
   ```

4. Mount the host model directory as `/models:ro` and inject the values from
   `cloud-a100.env.example` through the platform secret/config mechanism.
5. Expose only gateway port `8080`. Giga `8081` and Qwen `8082` bind to
   `127.0.0.1` and are never published.
6. Point Knowledge at the gateway's internal HTTPS/service address. The
   browser remains unaware of inference and no end-user auth is implemented by
   this internal gateway.
7. Wait for `/health/ready` before routing traffic. A listening socket alone is
   not readiness; required embedding and generation smokes must pass.

For a Docker-compatible operator shell, the equivalent launch shape is:

```bash
docker run --rm --gpus all --env-file cloud-a100.env \
  --mount type=bind,src=/models,dst=/models,readonly \
  --publish 8080:8080 <CLOUDRU_REGISTRY>/tenderhack-inference:<git-sha>
```

Cloud.ru Docker RUN supplies the equivalent GPU, read-only mount, port and
restart settings through its instance configuration; do not publish ports
8081/8082.

The container runs as UID/GID `1000:1000`, writes only under its private runtime
directory, and forwards logs to stdout/stderr. SIGTERM/SIGINT stop the gateway
and both persistent llama-server children without broad `killall`/`pkill`.

## Manual smoke

After a real deployment (and only after model provisioning), run from inside
the image or a trusted operator host:

```bash
INFERENCE_BASE_URL=http://127.0.0.1:8080 \
  /opt/tenderhack/scripts/smoke.sh
```

The script checks liveness, readiness, capabilities, a 2048-dimensional
embedding and a non-empty chat response without printing request content.
