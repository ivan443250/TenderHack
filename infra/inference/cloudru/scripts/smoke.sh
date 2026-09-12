#!/usr/bin/env bash
set -Eeuo pipefail

base_url="${INFERENCE_BASE_URL:-http://127.0.0.1:8080}"
token="${INFERENCE_API_TOKEN:-}"
auth_args=()
if [[ -n "$token" ]]; then
  auth_args=(-H "Authorization: Bearer $token")
fi

curl --fail --silent --show-error "$base_url/health/live" >/dev/null
curl --fail --silent --show-error "$base_url/health/ready" >/dev/null

capabilities="$(curl --fail --silent --show-error "${auth_args[@]}" "$base_url/v1/capabilities")"
python3 -c 'import json, sys; value=json.loads(sys.argv[1]); assert value.get("profile") in {"REDUCED", "FULL"}' "$capabilities"

embedding="$(curl --fail --silent --show-error "${auth_args[@]}" \
  -H 'Content-Type: application/json' \
  -d '{"model":"health-smoke","input":["Проверка доступности модели"]}' \
  "$base_url/v1/embeddings")"
python3 -c '
import json, math, sys
value=json.loads(sys.argv[1]); vector=value["data"][0]["embedding"]
assert len(vector) == 2048
assert all(math.isfinite(float(item)) for item in vector)
' "$embedding"

chat="$(curl --fail --silent --show-error "${auth_args[@]}" \
  -H 'Content-Type: application/json' \
  -d '{"model":"health-smoke","messages":[{"role":"user","content":"Ответь одним словом: готово"}],"temperature":0,"max_tokens":8}' \
  "$base_url/v1/chat/completions")"
python3 -c '
import json, sys
value=json.loads(sys.argv[1]); content=value["choices"][0]["message"]["content"]
assert content.strip()
assert "<think>" not in content.lower()
' "$chat"

printf 'gateway smoke passed: %s\n' "$base_url"
