# Self-contained portable GPU image. Build with infra/inference as context so
# the reviewed gateway and supervision scripts are reused without copying the
# Cloud.ru profile or any application source into the image.
FROM ghcr.io/ggml-org/llama.cpp:server-cuda13-b10902@sha256:3a88f3dba5f665c0dcb51f68ec990293758ac518b433556013a2e849694c8cd8

USER root

ENV DEBIAN_FRONTEND=noninteractive \
    LD_LIBRARY_PATH=/app:/usr/local/nvidia/lib:/usr/local/nvidia/lib64:/usr/local/cuda/lib64 \
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

# The pinned base already has ubuntu:1000:1000. Reuse either numeric identity
# when present, and create only missing identities without relying on names.
RUN set -eux; \
    if ! getent group 1000 >/dev/null; then \
        runtime_group_name=tenderhack-runtime; \
        runtime_group_suffix=0; \
        while getent group "$runtime_group_name" >/dev/null; do \
            runtime_group_suffix=$((runtime_group_suffix + 1)); \
            runtime_group_name="tenderhack-runtime-$runtime_group_suffix"; \
        done; \
        groupadd --gid 1000 "$runtime_group_name"; \
    fi; \
    if ! getent passwd 1000 >/dev/null; then \
        runtime_user_name=tenderhack-runtime; \
        runtime_user_suffix=0; \
        while getent passwd "$runtime_user_name" >/dev/null; do \
            runtime_user_suffix=$((runtime_user_suffix + 1)); \
            runtime_user_name="tenderhack-runtime-$runtime_user_suffix"; \
        done; \
        useradd --uid 1000 --gid 1000 --create-home --shell /usr/sbin/nologin "$runtime_user_name"; \
    fi; \
    install -d -o 1000 -g 1000 /opt/tenderhack /opt/tenderhack/gateway /opt/tenderhack/scripts /opt/tenderhack/run /models

# Reuse the reviewed gateway and supervision/verification helpers. The model
# files below are intentionally part of this profile and are never mounted or
# downloaded at startup.
COPY cloudru/gateway/app.py /opt/tenderhack/gateway/app.py
COPY cloudru/entrypoint.sh /opt/tenderhack/entrypoint.sh
COPY cloudru/scripts/verify-models.sh /opt/tenderhack/scripts/verify-models.sh
COPY cloudru/scripts/smoke.sh /opt/tenderhack/scripts/smoke.sh
COPY bundled/model-manifest.json /opt/tenderhack/model-manifest.json
COPY bundled/models/giga-embeddings-0826-3b-q8_0.gguf /models/giga-embeddings-0826-3b-q8_0.gguf
COPY bundled/models/Qwen3.8-4B-Q6_K.gguf /models/Qwen3.8-4B-Q6_K.gguf

# Both hashes are checked against the tracked manifest during every build.
# Giga's expected hash is frozen by the model contract; Qwen is recorded only
# after a real download and is never guessed.
RUN python3 - <<'PY'
import hashlib
import json

manifest = json.load(open('/opt/tenderhack/model-manifest.json', encoding='utf-8-sig'))
for key, path in (
    ('giga', '/models/giga-embeddings-0826-3b-q8_0.gguf'),
    ('qwen', '/models/Qwen3.8-4B-Q6_K.gguf'),
):
    digest = hashlib.sha256()
    with open(path, 'rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    measured = digest.hexdigest()
    expected = manifest['models'][key]['sha256']
    if measured != expected:
        raise SystemExit(f'{key} SHA256 mismatch: manifest={expected}, measured={measured}')
if manifest['models']['giga']['sha256'] != '429f2d04a968ffe73137fe65c2e458a08236056168b905d208b4d81ecab08c22':
    raise SystemExit('Giga manifest does not contain the frozen expected SHA256')
print('model manifest and baked weights verified')
PY

RUN chmod 0555 /opt/tenderhack/entrypoint.sh /opt/tenderhack/scripts/*.sh \
    && chmod 0444 /opt/tenderhack/gateway/app.py /opt/tenderhack/model-manifest.json \
    && chown -R 1000:1000 /opt/tenderhack

USER 1000:1000
WORKDIR /opt/tenderhack

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=6 \
    CMD curl --fail --silent http://127.0.0.1:8080/health/ready || exit 1

ENTRYPOINT ["/opt/tenderhack/entrypoint.sh"]
