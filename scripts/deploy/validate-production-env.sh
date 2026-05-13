#!/usr/bin/env bash
# Validate production env presence without printing secret values.
set -euo pipefail

ENV_FILE="${1:-.env.production}"
if [ ! -f "$ENV_FILE" ]; then
  echo "[env] FATAL: missing $ENV_FILE" >&2
  exit 1
fi

required_keys=(
  POSTGRES_DB
  POSTGRES_USER
  POSTGRES_PASSWORD
  API_ALLOWED_HOSTS
  AUTHTOKENS__ISSUER
  AUTHTOKENS__AUDIENCE
  AUTHTOKENS__ACCESSTOKENSIGNINGKEY
  AUTHTOKENS__REFRESHTOKENSIGNINGKEY
  AUTHTOKENS__ACCESSTOKENLIFETIME
  AUTHTOKENS__REFRESHTOKENLIFETIME
  AUTHTOKENS__OTPLIFETIME
  AUTHTOKENS__AUTHENTICATORISSUER
  BREVO__APIKEY
  BREVO__FROMEMAIL
  BREVO__EMAILVERIFICATIONTEMPLATEID
  BREVO__PASSWORDRESETTEMPLATEID
  SMTP__HOST
  SMTP__PORT
  SMTP__FROMEMAIL
  SMTP__USERNAME
  SMTP__PASSWORD
  BILLING__STRIPE__SECRETKEY
  BILLING__STRIPE__SUCCESSURL
  BILLING__STRIPE__CANCELURL
  BILLING__STRIPE__WEBHOOKSECRET
  BILLING__ALLOWSANDBOXFALLBACKS
  PUBLIC_API_BASE_URL
  CHECKOUT_BASE_URL
  CORS_ALLOWED_ORIGINS
  NEXT_PUBLIC_API_BASE_URL
  APP_URL
  SENTRY_DSN
  NEXT_PUBLIC_SENTRY_DSN
  EVIDENCE_SIGNER_FINGERPRINT
  BACKUP_GPG_PASSPHRASE
  BACKUP_S3_URL
  BACKUP_AWS_ACCESS_KEY_ID
  BACKUP_AWS_SECRET_ACCESS_KEY
  BACKUP_ALERT_WEBHOOK
  READING_SMOKE_LEARNER_EMAIL
  READING_SMOKE_LEARNER_PASSWORD
  READING_SMOKE_DISABLED_PAPER_ID
  READING_SMOKE_ENABLED_PAPER_ID
  READING_SMOKE_PROTECTED_MEDIA_ID
  READING_SMOKE_ENTITLED_MEDIA_ID
)

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

failed=0
duplicate_keys=$(grep -E '^[A-Za-z_][A-Za-z0-9_]*=' "$ENV_FILE" | cut -d= -f1 | sort | uniq -d || true)
if [ -n "$duplicate_keys" ]; then
  echo "[env] duplicate keys are not allowed:" >&2
  printf '%s\n' "$duplicate_keys" | sed 's/^/[env]   /' >&2
  failed=1
fi

for key in "${required_keys[@]}"; do
  value=$(read_env_value "$key" || true)
  if [ -z "$value" ]; then
    echo "[env] missing or empty: $key" >&2
    failed=1
    continue
  fi
  normalized_value=$(printf '%s' "$value" | tr '[:upper:]' '[:lower:]')
  case "$normalized_value" in
    *__placeholder__*|*placeholder*|*todo*|*changeme*|*replace-with*|*replace_with*|*example.invalid*|*examplepublickey*|*exampleprivatekey*|*0123456789abcdef*)
      echo "[env] placeholder value: $key" >&2
      failed=1
      ;;
  esac
done

if grep -niE '^[[:space:]]*[^#].*(__placeholder__|placeholder|todo|changeme|replace-with|replace_with|example\.invalid|examplepublickey|exampleprivatekey|0123456789abcdef)' "$ENV_FILE" >/tmp/oet-env-placeholder-lines.txt; then
  echo "[env] placeholder markers found in $ENV_FILE:" >&2
  cut -d: -f1 /tmp/oet-env-placeholder-lines.txt | sed 's/^/[env]   line /' >&2
  rm -f /tmp/oet-env-placeholder-lines.txt
  failed=1
fi
rm -f /tmp/oet-env-placeholder-lines.txt

require_min_length() {
  local key="$1"
  local min_length="$2"
  local value
  value=$(read_env_value "$key" || true)
  if [ "${#value}" -lt "$min_length" ]; then
    echo "[env] $key must be at least $min_length characters" >&2
    failed=1
  fi
}

require_https_url() {
  local key="$1"
  local value
  value=$(read_env_value "$key" || true)
  case "$value" in
    https://*) ;;
    *)
      echo "[env] $key must be an https:// URL" >&2
      failed=1
      ;;
  esac
  case "$value" in
    *localhost*|*127.0.0.1*|*0.0.0.0*)
      echo "[env] $key must not point at localhost in production" >&2
      failed=1
      ;;
  esac
}

require_no_wildcard_or_localhost() {
  local key="$1"
  local value
  value=$(read_env_value "$key" || true)
  case "$value" in
    "*"|*"*"*|*localhost*|*127.0.0.1*|*0.0.0.0*|http://*)
      echo "[env] $key must not contain wildcard, localhost, or http:// entries in production" >&2
      failed=1
      ;;
  esac
}

require_boolean_false() {
  local key="$1"
  local value
  value=$(read_env_value "$key" || true)
  value=$(printf '%s' "$value" | tr '[:upper:]' '[:lower:]')
  if [ "$value" != "false" ]; then
    echo "[env] $key must be false in production" >&2
    failed=1
  fi
}

require_s3_url() {
  local key="$1"
  local value
  value=$(read_env_value "$key" || true)
  case "$value" in
    s3://*) ;;
    *)
      echo "[env] $key must be an s3:// URL" >&2
      failed=1
      ;;
  esac
}

require_email() {
  local key="$1"
  local value
  value=$(read_env_value "$key" || true)
  case "$value" in
    *@*.*) ;;
    *)
      echo "[env] $key must be an email address" >&2
      failed=1
      ;;
  esac
}

require_hex_fingerprint() {
  local key="$1"
  local value length
  value=$(read_env_value "$key" || true)
  case "$value" in
    *[!A-Fa-f0-9]*)
      echo "[env] $key must be hexadecimal" >&2
      failed=1
      return
      ;;
  esac
  length=${#value}
  if [ "$length" -ne 40 ] && [ "$length" -ne 64 ]; then
    echo "[env] $key must be a full 40- or 64-character fingerprint" >&2
    failed=1
  fi
}

require_min_length AUTHTOKENS__ACCESSTOKENSIGNINGKEY 32
require_min_length AUTHTOKENS__REFRESHTOKENSIGNINGKEY 32
require_min_length POSTGRES_PASSWORD 16
require_min_length BILLING__STRIPE__WEBHOOKSECRET 16
require_min_length BACKUP_GPG_PASSPHRASE 16
require_min_length BACKUP_AWS_ACCESS_KEY_ID 8
require_min_length BACKUP_AWS_SECRET_ACCESS_KEY 16
require_min_length READING_SMOKE_LEARNER_PASSWORD 12
require_email READING_SMOKE_LEARNER_EMAIL
require_https_url PUBLIC_API_BASE_URL
require_https_url CHECKOUT_BASE_URL
require_https_url NEXT_PUBLIC_API_BASE_URL
require_https_url APP_URL
require_https_url SENTRY_DSN
require_https_url NEXT_PUBLIC_SENTRY_DSN
require_https_url BACKUP_ALERT_WEBHOOK
require_s3_url BACKUP_S3_URL
require_no_wildcard_or_localhost API_ALLOWED_HOSTS
require_no_wildcard_or_localhost CORS_ALLOWED_ORIGINS
require_boolean_false BILLING__ALLOWSANDBOXFALLBACKS
require_hex_fingerprint EVIDENCE_SIGNER_FINGERPRINT

if [ "$failed" -ne 0 ]; then
  echo "[env] validation failed" >&2
  exit 1
fi

echo "[env] validation passed for $ENV_FILE (${#required_keys[@]} required keys checked)"
