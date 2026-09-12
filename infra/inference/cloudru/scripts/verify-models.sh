#!/usr/bin/env bash
set -Eeuo pipefail

readonly GIGA_EXPECTED_SHA256="429f2d04a968ffe73137fe65c2e458a08236056168b905d208b4d81ecab08c22"

if [[ "$#" -ne 2 ]]; then
  printf 'usage: %s GIGA_MODEL_PATH QWEN_MODEL_PATH\n' "$0" >&2
  exit 64
fi

giga_path="$1"
qwen_path="$2"
for path in "$giga_path" "$qwen_path"; do
  if [[ ! -f "$path" || ! -r "$path" ]]; then
    printf 'model is missing or unreadable: %s\n' "$path" >&2
    exit 66
  fi
done

giga_sha256="$(sha256sum "$giga_path" | awk '{print $1}')"
qwen_sha256="$(sha256sum "$qwen_path" | awk '{print $1}')"
if [[ "$giga_sha256" != "$GIGA_EXPECTED_SHA256" ]]; then
  printf 'Giga SHA256 mismatch: expected %s, measured %s\n' "$GIGA_EXPECTED_SHA256" "$giga_sha256" >&2
  exit 65
fi

printf 'Giga SHA256 verified: %s\n' "$giga_sha256"
printf 'Qwen SHA256 measured (no frozen expected hash): %s\n' "$qwen_sha256"
