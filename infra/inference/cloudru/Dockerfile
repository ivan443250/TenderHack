# Cloud.ru Docker RUN inference image.
#
# The base is an official llama.cpp CUDA server image pinned by tag and
# manifest digest.  The digest was checked against the ggml-org container
# registry when this package was prepared; re-verify it before a production
# push.  Model weights are never copied into this image.
FROM ghcr.io/ggml-org/llama.cpp:server-cuda13-b10902@sha256:3a88f3dba5f665c0dcb51f68ec990293758ac518b433556013a2e849694c8cd8

USER root

ENV DEBIAN_FRONTEND=noninteractive \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    GIGA_MODEL_PATH=/models/giga-embeddings-0826-3b-q8_0.gguf \
    QWEN_MODEL_PATH=/models/Qwen3.8-4B-Q6_K.gguf \
    GIGA_PORT=8081 \
    QWEN_PORT=8082 \
    GATEWAY_PORT=8080 \
    GATEWAY_HOST=0.0.0.0 \
    GIGA_HOST=127.0.0.1 \
    QWEN_HOST=127.0.0.1 \
    GIGA_EMBEDDING_DIMENSION=2048 \
    RERANKER_ENABLED=false \
    GENERATOR_CONTEXT=8192 \
    GENERATOR_PARALLEL=4 \
    EMBEDDING_BATCH=8 \
    LLAMA_GPU_LAYERS=99 \
    STARTUP_TIMEOUT_SECONDS=300 \
    UPSTREAM_TIMEOUT_SECONDS=30 \
    LOG_CONTENT=false

# The CUDA server base is Ubuntu-based and already contains llama-server and
# its CUDA runtime libraries.  Python is installed without pip or model
# packages: the gateway intentionally uses only the standard library.
RUN apt-get update \
    && apt-get install --no-install-recommends -y \
        bash \
        ca-certificates \
        coreutils \
        curl \
        passwd \
        python3 \
    && rm -rf /var/lib/apt/lists/* /tmp/* /var/tmp/* \
    && test -x /app/llama-server

RUN groupadd --gid 1000 tenderhack \
    && useradd --uid 1000 --gid 1000 --create-home --shell /usr/sbin/nologin tenderhack \
    && install -d -o 1000 -g 1000 /opt/tenderhack /opt/tenderhack/gateway /opt/tenderhack/scripts /opt/tenderhack/run

COPY gateway/app.py /opt/tenderhack/gateway/app.py
COPY entrypoint.sh /opt/tenderhack/entrypoint.sh
COPY scripts/verify-models.sh /opt/tenderhack/scripts/verify-models.sh
COPY scripts/smoke.sh /opt/tenderhack/scripts/smoke.sh

RUN chmod 0555 /opt/tenderhack/entrypoint.sh /opt/tenderhack/scripts/*.sh \
    && chmod 0444 /opt/tenderhack/gateway/app.py \
    && chown -R 1000:1000 /opt/tenderhack

USER 1000:1000
WORKDIR /opt/tenderhack

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=6 \
    CMD curl --fail --silent http://127.0.0.1:8080/health/ready || exit 1

ENTRYPOINT ["/opt/tenderhack/entrypoint.sh"]
