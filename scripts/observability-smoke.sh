#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:3000}"
API_BASE_URL="${API_BASE_URL:-}"
OUT_PATH="${OBSERVABILITY_SMOKE_OUTPUT:-release-evidence/observability-smoke.json}"
CURL_CONNECT_TIMEOUT_SECONDS="${CURL_CONNECT_TIMEOUT_SECONDS:-5}"
CURL_MAX_TIME_SECONDS="${CURL_MAX_TIME_SECONDS:-15}"
mkdir -p "$(dirname "$OUT_PATH")"

targets=(
  "health|web_health|/api/health|${BASE_URL%/}/api/health"
  "page|web_home|/|${BASE_URL%/}/"
  "page|web_sign_in|/sign-in|${BASE_URL%/}/sign-in"
)

if [ -n "$API_BASE_URL" ]; then
  targets+=("health|api_ready|/health/ready|${API_BASE_URL%/}/health/ready")
fi

smoke_failed=0
checked_at_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)

echo "[" > "$OUT_PATH"
first=1
for target in "${targets[@]}"; do
  IFS='|' read -r kind name path url <<< "$target"
  tmp_file=$(mktemp)
  http_code=0
  time_total=0
  curl_redirect_args=()

  if [ "$kind" = "page" ]; then
    curl_redirect_args=(-L)
  fi

  if metrics=$(curl "${curl_redirect_args[@]}" -sS \
      --connect-timeout "$CURL_CONNECT_TIMEOUT_SECONDS" \
      --max-time "$CURL_MAX_TIME_SECONDS" \
      -o "$tmp_file" \
      -w '%{http_code} %{time_total}' \
      "$url" 2>/dev/null); then
    read -r http_code time_total <<< "$metrics"
  else
    smoke_failed=1
  fi
  rm -f "$tmp_file"

  if [ "$kind" = "health" ]; then
    if [ "${http_code:-0}" -lt 200 ] || [ "${http_code:-0}" -ge 300 ]; then
      smoke_failed=1
    fi
  elif [ "${http_code:-0}" -lt 200 ] || [ "${http_code:-0}" -ge 400 ]; then
    smoke_failed=1
  fi

  if [ "$first" -eq 0 ]; then
    echo "," >> "$OUT_PATH"
  fi
  first=0
  printf '  {"kind":"%s","name":"%s","path":"%s","url":"%s","status":%s,"timeTotalSeconds":%s,"checkedAtUtc":"%s"}' \
    "$kind" "$name" "$path" "$url" "${http_code:-0}" "${time_total:-0}" "$checked_at_utc" >> "$OUT_PATH"
done

echo "" >> "$OUT_PATH"
echo "]" >> "$OUT_PATH"
echo "Observability smoke written to $OUT_PATH"

if [ "$smoke_failed" -ne 0 ]; then
  echo "One or more observability smoke targets failed." >&2
  exit 1
fi
