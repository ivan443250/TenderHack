# Stable runtime profile for a persistent RunPod A40 Pod.
# The parent image already contains the verified models, gateway and entrypoint.
FROM docker.io/xedyardx/tenderhack-inference:dd559c5e

# Keep inherited model paths, ports, gateway settings and extra llama arguments.
# Only override the resource/runtime defaults that are appropriate for A40.
ENV GENERATOR_CONTEXT=8192 \
    GENERATOR_PARALLEL=1 \
    EMBEDDING_BATCH=2048 \
    LLAMA_GPU_LAYERS=99 \
    STARTUP_TIMEOUT_SECONDS=600 \
    UPSTREAM_TIMEOUT_SECONDS=60 \
    RERANKER_ENABLED=false \
    LOG_CONTENT=false

# Readiness is an expensive model smoke.  Container health must stay cheap and
# must never create recurring Giga/Qwen inference load.
HEALTHCHECK --interval=10s --timeout=3s --start-period=60s --retries=6 \
    CMD curl --fail --silent http://127.0.0.1:8080/ping || exit 1
