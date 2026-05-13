#!/usr/bin/env bash
# Reading/media production smoke gate. Requires fixture IDs/tokens in .env.production.
set -euo pipefail

APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
ENV_FILE="${ENV_FILE:-.env.production}"
cd "$APP_DIR"

read_env_value() {
  local key="$1"
  local line value
  line=$(grep -E "^${key}=" "$ENV_FILE" | tail -n 1 || true)
  if [ -z "$line" ]; then
    return 1
  fi
  value="${line#*=}"
  value="${value%\"}"
  value="${value#\"}"
  value="${value%\'}"
  value="${value#\'}"
  printf '%s' "$value"
}

required_env() {
  local key="$1"
  local value
  value="${!key:-}"
  if [ -z "$value" ]; then
    value=$(read_env_value "$key" || true)
  fi
  if [ -z "$value" ]; then
    echo "[reading-smoke] missing required $key" >&2
    exit 1
  fi
  printf '%s' "$value"
}

API_PUBLIC_URL="${API_PUBLIC_URL:-$(read_env_value PUBLIC_API_BASE_URL || true)}"
API_PUBLIC_URL="${API_PUBLIC_URL%/}"
if [ -z "$API_PUBLIC_URL" ]; then
  echo "[reading-smoke] missing API_PUBLIC_URL/PUBLIC_API_BASE_URL" >&2
  exit 1
fi

SMOKE_EMAIL="$(required_env READING_SMOKE_LEARNER_EMAIL)"
SMOKE_PASSWORD="$(required_env READING_SMOKE_LEARNER_PASSWORD)"
DISABLED_PAPER_ID="$(required_env READING_SMOKE_DISABLED_PAPER_ID)"
ENABLED_PAPER_ID="$(required_env READING_SMOKE_ENABLED_PAPER_ID)"
PROTECTED_MEDIA_ID="$(required_env READING_SMOKE_PROTECTED_MEDIA_ID)"
ENTITLED_MEDIA_ID="$(required_env READING_SMOKE_ENTITLED_MEDIA_ID)"

json_escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

echo "[reading-smoke] minting short-lived learner token"
sign_in_payload=$(printf '{"email":"%s","password":"%s","rememberMe":false}' "$(json_escape "$SMOKE_EMAIL")" "$(json_escape "$SMOKE_PASSWORD")")
sign_in_response=$(curl --fail --show-error --silent --max-time 20 \
  -H 'Content-Type: application/json' \
  -d "$sign_in_payload" \
  "$API_PUBLIC_URL/v1/auth/sign-in")
TOKEN=$(printf '%s' "$sign_in_response" | sed -n 's/.*"accessToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)
if [ -z "$TOKEN" ]; then
  echo "[reading-smoke] sign-in response did not include accessToken" >&2
  exit 1
fi
AUTH_HEADER="Authorization: Bearer $TOKEN"

curl_body() {
  local path="$1"
  curl --fail --show-error --silent --max-time 20 -H "$AUTH_HEADER" "$API_PUBLIC_URL$path"
}

curl_code() {
  local path="$1"
  curl --show-error --silent --max-time 20 -o /tmp/oet-reading-smoke-body.txt -w '%{http_code}' -H "$AUTH_HEADER" "$API_PUBLIC_URL$path"
}

assert_no_leaks() {
  local payload="$1"
  if printf '%s' "$payload" | grep -Eq 'CorrectAnswerJson|AcceptedSynonymsJson|ExplanationMarkdown|correctAnswer|acceptedSynonyms|explanationMarkdown|isCorrect'; then
    echo "[reading-smoke] learner structure leaked answer/explanation fields" >&2
    exit 1
  fi
}

echo "[reading-smoke] checking disabled paper-mode structure"
disabled_structure="$(curl_body "/v1/reading-papers/papers/$DISABLED_PAPER_ID/structure")"
assert_no_leaks "$disabled_structure"
if printf '%s' "$disabled_structure" | grep -Eq '"questionPaperAssets"[[:space:]]*:[[:space:]]*\[[[:space:]]*\{'; then
  echo "[reading-smoke] disabled paper mode exposed questionPaperAssets" >&2
  exit 1
fi

echo "[reading-smoke] checking enabled paper-mode structure"
enabled_structure="$(curl_body "/v1/reading-papers/papers/$ENABLED_PAPER_ID/structure")"
assert_no_leaks "$enabled_structure"
if ! printf '%s' "$enabled_structure" | grep -Eq '"questionPaperAssets"[[:space:]]*:[[:space:]]*\[[[:space:]]*\{'; then
  echo "[reading-smoke] enabled paper mode did not expose expected questionPaperAssets" >&2
  exit 1
fi

echo "[reading-smoke] checking protected media denial"
protected_content_code="$(curl_code "/v1/media/$PROTECTED_MEDIA_ID/content")"
protected_url_code="$(curl_code "/v1/media/$PROTECTED_MEDIA_ID/url")"
if [ "$protected_content_code" != "404" ] || [ "$protected_url_code" != "404" ]; then
  echo "[reading-smoke] protected media expected 404/404, got $protected_content_code/$protected_url_code" >&2
  exit 1
fi

echo "[reading-smoke] checking entitled source media access"
entitled_content_code="$(curl_code "/v1/media/$ENTITLED_MEDIA_ID/content")"
entitled_url_code="$(curl_code "/v1/media/$ENTITLED_MEDIA_ID/url")"
case "$entitled_content_code/$entitled_url_code" in
  2*/2*) ;;
  *)
    echo "[reading-smoke] entitled media expected 2xx/2xx, got $entitled_content_code/$entitled_url_code" >&2
    exit 1
    ;;
esac

echo "[reading-smoke] checking legacy Reading API shutdown"
legacy_code="$(curl_code "/v1/reading/home")"
if [ "$legacy_code" != "410" ]; then
  echo "[reading-smoke] legacy /v1/reading/home expected 410, got $legacy_code" >&2
  exit 1
fi

rm -f /tmp/oet-reading-smoke-body.txt
echo "[reading-smoke] PASS"