# TenderHack RunPod GPU profile

This profile is a thin derived image for one persistent, team-controlled
RunPod GPU Pod. It reuses the frozen image
`docker.io/xedyardx/tenderhack-inference:dd559c5e`; models, gateway code,
entrypoint, ports and inherited model arguments are not copied or downloaded
again.

Runtime overrides target an A40-class GPU:

- `LLAMA_GPU_LAYERS=99`
- `GENERATOR_CONTEXT=8192`
- `GENERATOR_PARALLEL=1`
- `EMBEDDING_BATCH=2048`
- `UPSTREAM_TIMEOUT_SECONDS=60`
- `STARTUP_TIMEOUT_SECONDS=600`
- `RERANKER_ENABLED=false`
- `LOG_CONTENT=false`

The Docker healthcheck calls the cheap `GET /ping` endpoint every 10 seconds.
It deliberately does not poll `/health/ready`, because readiness performs
real Giga/Qwen inference and is reserved for an explicit diagnostic check.

Expose only `8080/http` for the gateway. Keep `INFERENCE_API_TOKEN` as a Pod
secret when authentication is enabled; never commit or print its value.

Example build (from the repository root):

```text
docker buildx build --platform linux/amd64 \
  -f infra/inference/runpod/Dockerfile \
  -t docker.io/xedyardx/tenderhack-inference:runpod-<short-sha> \
  --push .
```
