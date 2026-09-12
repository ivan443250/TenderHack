# Local RTX 3060 Laptop profile.  This deliberately uses a CUDA 12.8 runtime
# compatible with the local host driver; the Cloud.ru/A100 Dockerfile remains
# unchanged.  Build context is infra/inference so the reviewed gateway and
# model artifacts can be reused without duplicating scripts.
FROM ghcr.io/ggml-org/llama.cpp:server-cuda12-b9426@sha256:e694ec5a1b19456e340fea9a28ffa169d6952c0019f39b6c09f7e92f08bc5014

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
    GENERATOR_CONTEXT=4096 \
    GENERATOR_PARALLEL=1 \
    EMBEDDING_BATCH=2048 \
    GIGA_EXTRA_ARGS="--ctx-size 2048 --parallel 1 --no-warmup" \
    QWEN_EXTRA_ARGS="--reasoning off" \
    LLAMA_GPU_LAYERS=1 \
    STARTUP_TIMEOUT_SECONDS=600 \
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

# Keep the portable runtime at UID/GID 1000:1000 without depending on the
# distro's account name.  The base image already has this numeric identity,
# but the checks make the Dockerfile robust to a future base refresh.
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

# Reuse the reviewed gateway, supervision and model verification logic.  The
# model files are copied from the already verified bundled profile and remain
# ignored by Git.
COPY cloudru/gateway/app.py /opt/tenderhack/gateway/app.py
COPY cloudru/entrypoint.sh /opt/tenderhack/entrypoint.sh
COPY cloudru/scripts/verify-models.sh /opt/tenderhack/scripts/verify-models.sh
COPY cloudru/scripts/smoke.sh /opt/tenderhack/scripts/smoke.sh
COPY bundled/model-manifest.json /opt/tenderhack/model-manifest.json
COPY bundled/models/giga-embeddings-0826-3b-q8_0.gguf /models/giga-embeddings-0826-3b-q8_0.gguf
COPY bundled/models/Qwen3.8-4B-Q6_K.gguf /models/Qwen3.8-4B-Q6_K.gguf

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

HEALTHCHECK --interval=10s --timeout=5s --start-period=60s --retries=6 \
    CMD curl --fail --silent http://127.0.0.1:8080/health/ready || exit 1

ENTRYPOINT ["/opt/tenderhack/entrypoint.sh"]
