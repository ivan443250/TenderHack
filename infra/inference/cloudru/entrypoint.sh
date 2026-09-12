#!/usr/bin/env bash
set -Eeuo pipefail

GIGA_PID=""
QWEN_PID=""
GATEWAY_PID=""

log() {
  printf '%s\n' "[tenderhack-inference] $*"
}

cleanup() {
  local status=$?
  trap - EXIT INT TERM
  for pid in "$GATEWAY_PID" "$QWEN_PID" "$GIGA_PID"; do
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      kill -TERM "$pid" 2>/dev/null || true
    fi
  done
  for pid in "$GATEWAY_PID" "$QWEN_PID" "$GIGA_PID"; do
    if [[ -n "$pid" ]]; then
      wait "$pid" 2>/dev/null || true
    fi
  done
  exit "$status"
}

trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

require_positive_int() {
  local name="$1"
  local value="$2"
  if [[ ! "$value" =~ ^[1-9][0-9]*$ ]]; then
    log "$name must be a positive integer"
    exit 64
  fi
}

require_file() {
  local name="$1"
  local path="$2"
  if [[ ! -f "$path" ]]; then
    log "$name is missing: $path"
    exit 66
  fi
}

append_words() {
  local raw="$1"
  local -n target="$2"
  if [[ -n "$raw" ]]; then
    local -a words=()
    read -r -a words <<< "$raw"
    target+=("${words[@]}")
  fi
}

wait_for_health() {
  local name="$1"
  local url="$2"
  local pid="$3"
  local deadline=$((SECONDS + STARTUP_TIMEOUT_SECONDS))
  while (( SECONDS < deadline )); do
    if ! kill -0 "$pid" 2>/dev/null; then
      log "$name exited before health became ready"
      exit 70
    fi
    if curl --fail --silent --show-error --max-time 2 "$url" >/dev/null 2>&1; then
      log "$name process is reachable"
      return 0
    fi
    sleep 1
  done
  log "$name health timeout after ${STARTUP_TIMEOUT_SECONDS}s"
  exit 70
}

wait_for_gateway_ready() {
  local deadline=$((SECONDS + STARTUP_TIMEOUT_SECONDS))
  while (( SECONDS < deadline )); do
    if ! kill -0 "$GATEWAY_PID" 2>/dev/null; then
      log "gateway exited before readiness"
      exit 70
    fi
    if curl --fail --silent --show-error --max-time 3 "http://127.0.0.1:${GATEWAY_PORT}/health/ready" >/dev/null 2>&1; then
      log "gateway readiness smoke passed"
      return 0
    fi
    sleep 1
  done
  log "gateway readiness timeout after ${STARTUP_TIMEOUT_SECONDS}s"
  exit 70
}

require_positive_int GIGA_PORT "$GIGA_PORT"
require_positive_int QWEN_PORT "$QWEN_PORT"
require_positive_int GATEWAY_PORT "$GATEWAY_PORT"
require_positive_int STARTUP_TIMEOUT_SECONDS "$STARTUP_TIMEOUT_SECONDS"
require_positive_int EMBEDDING_BATCH "$EMBEDDING_BATCH"
require_positive_int GENERATOR_CONTEXT "$GENERATOR_CONTEXT"
require_positive_int GENERATOR_PARALLEL "$GENERATOR_PARALLEL"
require_positive_int LLAMA_GPU_LAYERS "$LLAMA_GPU_LAYERS"
if (( GENERATOR_CONTEXT > 8192 )); then
  log "GENERATOR_CONTEXT must not exceed the project cap of 8192"
  exit 64
fi

require_file GIGA_MODEL_PATH "$GIGA_MODEL_PATH"
require_file QWEN_MODEL_PATH "$QWEN_MODEL_PATH"
require_file llama-server /app/llama-server

if [[ ! -r "$GIGA_MODEL_PATH" || ! -r "$QWEN_MODEL_PATH" ]]; then
  log "model files must be readable; /models is expected to be mounted read-only"
  exit 66
fi

/opt/tenderhack/scripts/verify-models.sh "$GIGA_MODEL_PATH" "$QWEN_MODEL_PATH"

server_help="$(/app/llama-server --help 2>&1 || true)"
for required_flag in \
  "--embeddings" \
  "--host" \
  "--port" \
  "--model" \
  "--n-gpu-layers" \
  "--batch-size" \
  "--ubatch-size" \
  "--ctx-size" \
  "--parallel" \
  "--jinja"; do
  if ! grep -Fq -- "$required_flag" <<< "$server_help"; then
    log "pinned llama-server does not advertise required flag $required_flag"
    exit 64
  fi
done
if [[ "${FLASH_ATTENTION:-false}" == "true" ]] && ! grep -Eq -- '(^|[ ,])-fa,|--flash-attn' <<< "$server_help"; then
  log "FLASH_ATTENTION=true but the pinned llama-server help has no flash-attn flag"
  exit 64
fi

log "llama-server version: $(/app/llama-server --version 2>&1 | head -n 1 || true)"

giga_cmd=(
  /app/llama-server
  --model "$GIGA_MODEL_PATH"
  --embeddings
  --host "$GIGA_HOST"
  --port "$GIGA_PORT"
  --n-gpu-layers "$LLAMA_GPU_LAYERS"
  --batch-size "$EMBEDDING_BATCH"
  --ubatch-size "$EMBEDDING_BATCH"
)
if [[ "${FLASH_ATTENTION:-false}" == "true" ]]; then
  giga_cmd+=(--flash-attn)
fi
append_words "${GIGA_EXTRA_ARGS:-}" giga_cmd

qwen_cmd=(
  /app/llama-server
  --model "$QWEN_MODEL_PATH"
  --jinja
  --ctx-size "$GENERATOR_CONTEXT"
  --parallel "$GENERATOR_PARALLEL"
  --host "$QWEN_HOST"
  --port "$QWEN_PORT"
  --n-gpu-layers "$LLAMA_GPU_LAYERS"
)
if [[ "${FLASH_ATTENTION:-false}" == "true" ]]; then
  qwen_cmd+=(--flash-attn)
fi
append_words "${QWEN_EXTRA_ARGS:-}" qwen_cmd

log "starting persistent Giga llama-server on ${GIGA_HOST}:${GIGA_PORT}"
"${giga_cmd[@]}" &
GIGA_PID=$!
wait_for_health Giga "http://127.0.0.1:${GIGA_PORT}/health" "$GIGA_PID"

log "starting persistent Qwen llama-server on ${QWEN_HOST}:${QWEN_PORT}"
"${qwen_cmd[@]}" &
QWEN_PID=$!
wait_for_health Qwen "http://127.0.0.1:${QWEN_PORT}/health" "$QWEN_PID"

export EMBEDDING_URL="http://127.0.0.1:${GIGA_PORT}"
export GENERATOR_URL="http://127.0.0.1:${QWEN_PORT}"
log "starting gateway on ${GATEWAY_HOST}:${GATEWAY_PORT}"
python3 /opt/tenderhack/gateway/app.py &
GATEWAY_PID=$!
wait_for_gateway_ready

log "inference container is ready (profile is reported by /health/ready)"
if ! wait -n "$GATEWAY_PID" "$QWEN_PID" "$GIGA_PID"; then
  log "a child process exited unexpectedly"
  exit 70
fi
log "a child process exited unexpectedly"
exit 70
