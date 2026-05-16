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
  AI__BASEURL
  AI__APIKEY
  AI__PROVIDERID
  AI__DEFAULTMODEL
  PRONUNCIATION__PROVIDER
  PRONUNCIATION__AZURESPEECHKEY
  PRONUNCIATION__AZURESPEECHREGION
  CONVERSATION__ENABLED
  CONVERSATION__ASRPROVIDER
  CONVERSATION__DEEPGRAMAPIKEY
  CONVERSATION__TTSPROVIDER
  CONVERSATION__ELEVENLABSAPIKEY
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
  ROUTER_IMAGE
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

read_env_value_ci() {
  local key="$1"
  local target value
  target=$(printf '%s' "$key" | tr '[:upper:]' '[:lower:]')
  value=$(awk -F= -v target="$target" '
    /^[[:space:]]*($|#)/ { next }
    /^[[:space:]]*[A-Za-z_][A-Za-z0-9_]*[[:space:]]*=/ {
      k = $1
      gsub(/^[[:space:]]+|[[:space:]]+$/, "", k)
      if (tolower(k) == target) {
        v = $0
        sub(/^[^=]*=/, "", v)
        last = v
        found = 1
      }
    }
    END { if (found) print last; else exit 1 }
  ' "$ENV_FILE") || return 1
  value=$(printf '%s' "$value" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
  value="${value%\"}"
  value="${value#\"}"
  value="${value%\'}"
  value="${value#\'}"
  printf '%s' "$value"
}

read_env_value_any() {
  local primary_key="$1"
  local alt_key="${2:-}"
  read_env_value_ci "$primary_key" && return 0
  if [ -n "$alt_key" ]; then
    read_env_value_ci "$alt_key" && return 0
  fi
  return 1
}

failed=0
duplicate_keys=$(grep -E '^[A-Za-z_][A-Za-z0-9_]*=' "$ENV_FILE" | cut -d= -f1 | sort | uniq -d || true)
if [ -n "$duplicate_keys" ]; then
  echo "[env] duplicate keys are not allowed:" >&2
  printf '%s\n' "$duplicate_keys" | sed 's/^/[env]   /' >&2
  failed=1
fi

# Keys that are admin-configurable via UI (DB-backed) OR optional infrastructure.
# When empty in .env.production these warn but do not fail the validator —
# the backend either no-ops gracefully (Brevo/Sentry/Backup) or reads the live
# value from the admin-managed database (AI providers, Pronunciation,
# Conversation). READING_SMOKE_* are CI test fixtures, never runtime config.
ADMIN_OPTIONAL_KEYS="AI__APIKEY AI__BASEURL AI__DEFAULTMODEL AI__PROVIDERID BACKUP_ALERT_WEBHOOK BACKUP_AWS_ACCESS_KEY_ID BACKUP_AWS_SECRET_ACCESS_KEY BACKUP_GPG_PASSPHRASE BACKUP_S3_URL BREVO__APIKEY BREVO__EMAILVERIFICATIONTEMPLATEID BREVO__PASSWORDRESETTEMPLATEID CONVERSATION__ASRPROVIDER CONVERSATION__DEEPGRAMAPIKEY CONVERSATION__ELEVENLABSAPIKEY CONVERSATION__ENABLED CONVERSATION__TTSPROVIDER NEXT_PUBLIC_SENTRY_DSN PRONUNCIATION__AZURESPEECHKEY PRONUNCIATION__AZURESPEECHREGION PRONUNCIATION__PROVIDER READING_SMOKE_DISABLED_PAPER_ID READING_SMOKE_ENABLED_PAPER_ID READING_SMOKE_ENTITLED_MEDIA_ID READING_SMOKE_LEARNER_EMAIL READING_SMOKE_LEARNER_PASSWORD READING_SMOKE_PROTECTED_MEDIA_ID SENTRY_DSN"
is_admin_optional() {
  case " $ADMIN_OPTIONAL_KEYS " in *" $1 "*) return 0 ;; *) return 1 ;; esac
}

for key in "${required_keys[@]}"; do
  value=$(read_env_value "$key" || true)
  if [ -z "$value" ]; then
    if is_admin_optional "$key"; then
      echo "[env] (admin-configurable, ok to be empty) $key" >&2
      continue
    fi
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

placeholder_lines=$(grep -niE '^[[:space:]]*[^#].*(__placeholder__|placeholder|todo|changeme|replace-with|replace_with|example\.invalid|examplepublickey|exampleprivatekey|0123456789abcdef)' "$ENV_FILE" || true)
if [ -n "$placeholder_lines" ]; then
  echo "[env] placeholder markers found in $ENV_FILE:" >&2
  printf '%s\n' "$placeholder_lines" | cut -d: -f1 | sed 's/^/[env]   line /' >&2
  failed=1
fi

require_min_length() {
  local key="$1"
  local min_length="$2"
  local value
  value=$(read_env_value "$key" || true)
  if [ -z "$value" ] && is_admin_optional "$key"; then
    return 0
  fi
  if [ "${#value}" -lt "$min_length" ]; then
    echo "[env] $key must be at least $min_length characters" >&2
    failed=1
  fi
}

require_https_url() {
  local key="$1"
  local value
  value=$(read_env_value "$key" || true)
  if [ -z "$value" ] && is_admin_optional "$key"; then
    return 0
  fi
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

require_digest_image() {
  local key="$1"
  local value digest
  value=$(read_env_value "$key" || true)
  case "$value" in
    *@sha256:*) ;;
    *)
      echo "[env] $key must be pinned to an immutable @sha256 digest" >&2
      failed=1
      return
      ;;
  esac
  digest="${value##*@sha256:}"
  case "$digest" in
    *[!0-9a-fA-F]*)
      echo "[env] $key digest must be hexadecimal" >&2
      failed=1
      return
      ;;
  esac
  if [ "${#digest}" -ne 64 ]; then
    echo "[env] $key digest must be 64 hex characters" >&2
    failed=1
  fi
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

require_absent() {
  local key="$1"
  if read_env_value "$key" >/dev/null 2>&1; then
    echo "[env] $key must not be present in production env" >&2
    failed=1
  fi
}

require_absent_prefix() {
  local prefix="$1"
  if grep -Ei "^[[:space:]]*${prefix}[A-Za-z0-9_]*=" "$ENV_FILE" >/dev/null; then
    echo "[env] keys with prefix $prefix must not be present in production env" >&2
    failed=1
  fi
}

require_absent_key_regex_ci() {
  local key_regex="$1"
  local label="$2"
  if grep -Ei "^[[:space:]]*${key_regex}[[:space:]]*=" "$ENV_FILE" >/dev/null; then
    echo "[env] keys matching $label must not be present in production env" >&2
    failed=1
  fi
}

require_boolean_false_unless_acknowledged() {
  local key="$1"
  local alt_key="$2"
  local ack_key="$3"
  local alt_ack_key="${4:-}"
  local value ack_value
  value=$(read_env_value_ci "$key" || true)
  if [ -z "$value" ] && [ -n "$alt_key" ]; then
    value=$(read_env_value_ci "$alt_key" || true)
  fi
  value=$(printf '%s' "$value" | tr '[:upper:]' '[:lower:]')
  ack_value=$(read_env_value_ci "$ack_key" || true)
  if [ -z "$ack_value" ] && [ -n "$alt_ack_key" ]; then
    ack_value=$(read_env_value_ci "$alt_ack_key" || true)
  fi
  ack_value=$(printf '%s' "$ack_value" | tr '[:upper:]' '[:lower:]')
  if [ "$value" = "true" ] && [ "$ack_value" != "i-understand-realtime-stt-is-gated" ]; then
    echo "[env] $key may only be true with $ack_key=i-understand-realtime-stt-is-gated" >&2
    failed=1
  fi
}

require_not_value_prefix() {
  local key="$1"
  local alt_key="$2"
  local forbidden_prefix="$3"
  local value
  value=$(read_env_value_ci "$key" || true)
  if [ -z "$value" ] && [ -n "$alt_key" ]; then
    value=$(read_env_value_ci "$alt_key" || true)
  fi
  value=$(printf '%s' "$value" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' | tr '[:upper:]' '[:lower:]')
  if [[ "$value" == "$forbidden_prefix"* ]]; then
    echo "[env] $key/$alt_key must not use $forbidden_prefix* in production until the realtime provider adapter and protected smoke are implemented" >&2
    failed=1
  fi
}

require_not_value() {
  local key="$1"
  local alt_key="$2"
  local forbidden="$3"
  local value
  value=$(read_env_value_ci "$key" || true)
  if [ -z "$value" ] && [ -n "$alt_key" ]; then
    value=$(read_env_value_ci "$alt_key" || true)
  fi
  value=$(printf '%s' "$value" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' | tr '[:upper:]' '[:lower:]')
  if [ "$value" = "$forbidden" ]; then
    echo "[env] $key/$alt_key must not be $forbidden in production" >&2
    failed=1
  fi
}

require_s3_url() {
  local key="$1"
  local value
  value=$(read_env_value "$key" || true)
  if [ -z "$value" ] && is_admin_optional "$key"; then
    return 0
  fi
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
  if [ -z "$value" ] && is_admin_optional "$key"; then
    return 0
  fi
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
require_min_length AI__APIKEY 16
require_min_length PRONUNCIATION__AZURESPEECHKEY 16
require_min_length CONVERSATION__DEEPGRAMAPIKEY 16
require_min_length CONVERSATION__ELEVENLABSAPIKEY 16
require_min_length BACKUP_GPG_PASSPHRASE 16
require_min_length BACKUP_AWS_ACCESS_KEY_ID 8
require_min_length BACKUP_AWS_SECRET_ACCESS_KEY 16
require_min_length READING_SMOKE_LEARNER_PASSWORD 12
require_email READING_SMOKE_LEARNER_EMAIL
require_https_url PUBLIC_API_BASE_URL
require_https_url CHECKOUT_BASE_URL
require_https_url AI__BASEURL
require_https_url NEXT_PUBLIC_API_BASE_URL
require_https_url APP_URL
require_https_url SENTRY_DSN
require_https_url NEXT_PUBLIC_SENTRY_DSN
require_https_url BACKUP_ALERT_WEBHOOK
require_s3_url BACKUP_S3_URL
require_no_wildcard_or_localhost API_ALLOWED_HOSTS
require_no_wildcard_or_localhost CORS_ALLOWED_ORIGINS
require_digest_image ROUTER_IMAGE
require_boolean_false BILLING__ALLOWSANDBOXFALLBACKS
require_hex_fingerprint EVIDENCE_SIGNER_FINGERPRINT
require_absent NEXT_PUBLIC_ELEVENLABS_API_KEY
require_absent NEXT_PUBLIC_ELEVENLABS_STT_API_KEY
require_absent NEXT_PUBLIC_ELEVENLABS_KEY
require_absent_prefix NEXT_PUBLIC_ELEVENLABS
require_absent CONVERSATION__ELEVENLABSSTTAPIKEY
require_absent Conversation__ElevenLabsSttApiKey
require_absent_key_regex_ci 'conversation__elevenlabsstt[a-z0-9_]*apikey' 'Conversation__ElevenLabsStt*ApiKey'
require_not_value PRONUNCIATION__PROVIDER Pronunciation__Provider mock
require_not_value CONVERSATION__ASRPROVIDER Conversation__AsrProvider mock
require_not_value CONVERSATION__TTSPROVIDER Conversation__TtsProvider mock
require_not_value CONVERSATION__REALTIMEASRPROVIDER Conversation__RealtimeAsrProvider mock
require_not_value AI__PROVIDERID Ai__ProviderId mock
require_not_value AI__DEFAULTMODEL Ai__DefaultModel mock
require_not_value_prefix CONVERSATION__REALTIMEASRPROVIDER Conversation__RealtimeAsrProvider elevenlabs
require_boolean_false_unless_acknowledged CONVERSATION__REALTIMESTTENABLED Conversation__RealtimeSttEnabled CONVERSATION__REALTIMESTTROLLOUTACKNOWLEDGEMENT Conversation__RealtimeSttRolloutAcknowledgement
require_boolean_false_unless_acknowledged CONVERSATION__REALTIMESTTALLOWREALPROVIDER Conversation__RealtimeSttAllowRealProvider CONVERSATION__REALTIMESTTROLLOUTACKNOWLEDGEMENT Conversation__RealtimeSttRolloutAcknowledgement
require_boolean_false_unless_acknowledged CONVERSATION__REALTIMESTTREALPROVIDERPRODUCTIONAUTHORIZED Conversation__RealtimeSttRealProviderProductionAuthorized CONVERSATION__REALTIMESTTROLLOUTACKNOWLEDGEMENT Conversation__RealtimeSttRolloutAcknowledgement
require_not_value CONVERSATION__REALTIMESTTALLOWMANAGEDLEARNERREALPROVIDER Conversation__RealtimeSttAllowManagedLearnerRealProvider true

real_provider_enabled=$(read_env_value_any CONVERSATION__REALTIMESTTALLOWREALPROVIDER Conversation__RealtimeSttAllowRealProvider || true)
real_provider_enabled=$(printf '%s' "$real_provider_enabled" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' | tr '[:upper:]' '[:lower:]')
if [[ "$real_provider_enabled" == "true" ]]; then
  production_authorized=$(read_env_value_any CONVERSATION__REALTIMESTTREALPROVIDERPRODUCTIONAUTHORIZED Conversation__RealtimeSttRealProviderProductionAuthorized || true)
  production_authorized=$(printf '%s' "$production_authorized" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' | tr '[:upper:]' '[:lower:]')
  if [[ "$production_authorized" != "true" ]]; then
    echo "[env] Conversation__RealtimeSttRealProviderProductionAuthorized must be true when real realtime STT is enabled" >&2
    failed=1
  fi

  topology=$(read_env_value_any CONVERSATION__REALTIMESTTPROVIDERSESSIONTOPOLOGY Conversation__RealtimeSttProviderSessionTopology || true)
  topology=$(printf '%s' "$topology" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' | tr '[:upper:]' '[:lower:]')
  case "$topology" in
    single-instance|single-region-sticky|distributed) ;;
    *)
      echo "[env] Conversation__RealtimeSttProviderSessionTopology must be single-instance, single-region-sticky, or distributed when real realtime STT is enabled" >&2
      failed=1
      ;;
  esac

  region_id=$(read_env_value_any CONVERSATION__REALTIMESTTREGIONID Conversation__RealtimeSttRegionId || true)
  if [[ -z "$(printf '%s' "$region_id" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')" ]]; then
    echo "[env] Conversation__RealtimeSttRegionId is required when real realtime STT is enabled" >&2
    failed=1
  fi

  pricing=$(read_env_value_any CONVERSATION__REALTIMESTTESTIMATEDCOSTUSDPERMINUTE Conversation__RealtimeSttEstimatedCostUsdPerMinute || true)
  if ! awk -v pricing="$pricing" 'BEGIN { exit !(pricing ~ /^[0-9]+([.][0-9]+)?$/ && pricing + 0 > 0) }' 2>/dev/null; then
    echo "[env] Conversation__RealtimeSttEstimatedCostUsdPerMinute must be greater than 0 when real realtime STT is enabled" >&2
    failed=1
  fi
fi

if [ "$failed" -ne 0 ]; then
  echo "[env] validation failed" >&2
  exit 1
fi

echo "[env] validation passed for $ENV_FILE (${#required_keys[@]} required keys checked)"
