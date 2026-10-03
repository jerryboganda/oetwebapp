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
  BACKUP_GPG_PASSPHRASE
  BACKUP_S3_URL
  BACKUP_AWS_ACCESS_KEY_ID
  BACKUP_AWS_SECRET_ACCESS_KEY
  BACKUP_ALERT_WEBHOOK
  BACKUP_RESTORE_DRILL_ID
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
# Conversation). Backup settings are fail-closed separately unless the owner
# sets the explicit break-glass acknowledgement. READING_SMOKE_* are CI test
# fixtures, never runtime config.
ADMIN_OPTIONAL_KEYS="AI__APIKEY AI__BASEURL AI__DEFAULTMODEL AI__PROVIDERID BREVO__APIKEY BREVO__EMAILVERIFICATIONTEMPLATEID BREVO__PASSWORDRESETTEMPLATEID CONVERSATION__ASRPROVIDER CONVERSATION__DEEPGRAMAPIKEY CONVERSATION__ELEVENLABSAPIKEY CONVERSATION__ENABLED CONVERSATION__TTSPROVIDER NEXT_PUBLIC_SENTRY_DSN PRONUNCIATION__AZURESPEECHKEY PRONUNCIATION__AZURESPEECHREGION PRONUNCIATION__PROVIDER READING_SMOKE_DISABLED_PAPER_ID READING_SMOKE_ENABLED_PAPER_ID READING_SMOKE_ENTITLED_MEDIA_ID READING_SMOKE_LEARNER_EMAIL READING_SMOKE_LEARNER_PASSWORD READING_SMOKE_PROTECTED_MEDIA_ID SENTRY_DSN TYPESAFE__APIKEY"
is_admin_optional() {
  case " $ADMIN_OPTIONAL_KEYS " in *" $1 "*) return 0 ;; *) return 1 ;; esac
}

BACKUP_REQUIRED_KEYS="BACKUP_ALERT_WEBHOOK BACKUP_AWS_ACCESS_KEY_ID BACKUP_AWS_SECRET_ACCESS_KEY BACKUP_GPG_PASSPHRASE BACKUP_RESTORE_DRILL_ID BACKUP_S3_URL"
is_backup_required_key() {
  case " $BACKUP_REQUIRED_KEYS " in *" $1 "*) return 0 ;; *) return 1 ;; esac
}

backup_break_glass_acknowledged() {
  local ack
  ack=$(read_env_value_ci BACKUP_BREAK_GLASS_ACKNOWLEDGEMENT || true)
  ack=$(printf '%s' "$ack" | tr '[:upper:]' '[:lower:]')
  [ "$ack" = "i-accept-temporary-no-offsite-backup-risk" ]
}

allows_empty_key() {
  local key="$1"
  if is_admin_optional "$key"; then
    return 0
  fi
  if is_backup_required_key "$key" && backup_break_glass_acknowledged; then
    return 0
  fi
  return 1
}

for key in "${required_keys[@]}"; do
  value=$(read_env_value "$key" || true)
  if [ -z "$value" ]; then
    if allows_empty_key "$key"; then
      if is_backup_required_key "$key"; then
        echo "[env] BREAK-GLASS: $key is empty under BACKUP_BREAK_GLASS_ACKNOWLEDGEMENT" >&2
      else
        echo "[env] (admin-configurable, ok to be empty) $key" >&2
      fi
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
  if [ -z "$value" ] && allows_empty_key "$key"; then
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
  if [ -z "$value" ] && allows_empty_key "$key"; then
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

require_wss_url() {
  local key="$1"
  local value
  value=$(read_env_value "$key" || true)
  case "$value" in
    wss://*) ;;
    *)
      echo "[env] $key must be a wss:// URL" >&2
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

require_boolean_true() {
  local key="$1"
  local value
  value=$(read_env_value "$key" || true)
  value=$(printf '%s' "$value" | tr '[:upper:]' '[:lower:]')
  if [ "$value" != "true" ]; then
    echo "[env] $key must be true in production" >&2
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
  if [ -z "$value" ] && allows_empty_key "$key"; then
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
require_min_length BACKUP_RESTORE_DRILL_ID 8
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
require_boolean_false BILLING__ALLOWSANDBOXFALLBACKS
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

# Live AI voice, LiveKit tutor rooms and TypeSafe are OPTIONAL in production
# (owner decision 2026-09-23). A missing provider leaves that feature in its
# controlled "unavailable" state; it must never block the whole deploy. When a
# provider IS configured, its settings are validated in full.
live_voice_primary=$(read_env_value LIVEVOICE__PRIMARYPROVIDER || true)
live_voice_primary=$(printf '%s' "$live_voice_primary" | tr '[:upper:]' '[:lower:]')
live_voice_openai_key=$(read_env_value LIVEVOICE__OPENAIAPIKEY || true)
live_voice_gemini_key=$(read_env_value LIVEVOICE__GEMINIAPIKEY || true)
if [ -n "$live_voice_openai_key" ] || [ -n "$live_voice_gemini_key" ]; then
  case "$live_voice_primary" in
    openai|gemini) ;;
    *)
      echo "[env] LIVEVOICE__PRIMARYPROVIDER must be openai or gemini when a live voice key is set" >&2
      failed=1
      ;;
  esac
fi
if [ -n "$live_voice_openai_key" ]; then
  require_min_length LIVEVOICE__OPENAIAPIKEY 16
  require_https_url LIVEVOICE__OPENAIBASEURL
  require_https_url LIVEVOICE__OPENAIMODELSBASEURL
fi
if [ -n "$live_voice_gemini_key" ]; then
  require_min_length LIVEVOICE__GEMINIAPIKEY 16
  require_https_url LIVEVOICE__GEMINIBASEURL
  require_https_url LIVEVOICE__GEMINIMODELSBASEURL
  require_wss_url LIVEVOICE__GEMINIWEBSOCKETBASEURL
fi

livekit_provider=$(read_env_value LIVEKIT__PROVIDER || true)
livekit_provider=$(printf '%s' "$livekit_provider" | tr '[:upper:]' '[:lower:]')
case "$livekit_provider" in
  ""|disabled) ;;
  livekit_cloud)
    require_min_length LIVEKIT__APIKEY 8
    require_min_length LIVEKIT__APISECRET 16
    require_min_length LIVEKIT__WEBHOOKSIGNINGSECRET 16
    require_wss_url LIVEKIT__WSSURL
    livekit_egress=$(read_env_value LIVEKIT__EGRESSENABLED || true)
    if [ "$(printf '%s' "$livekit_egress" | tr '[:upper:]' '[:lower:]')" = "true" ]; then
      require_s3_url LIVEKIT__EGRESSBUCKET
      require_min_length LIVEKIT__EGRESSACCESSKEY 8
      require_min_length LIVEKIT__EGRESSSECRET 16
    fi
    ;;
  *)
    echo "[env] LIVEKIT__PROVIDER must be livekit_cloud or disabled" >&2
    failed=1
    ;;
esac

# TypeSafe (Jev) tuning knobs are all optional: an unset key falls back to the
# default docker-compose.production.yml forwards. A value that IS set must be
# sane, because a bad threshold silently turns a judgment gate off (0) or makes
# it unreachable (>1). Reference: docs/env/typesafe.md.
require_unit_interval_if_set() {
  local key="$1"
  local value
  value=$(read_env_value "$key" || true)
  if [ -z "$value" ]; then
    return 0
  fi
  if ! awk -v v="$value" 'BEGIN { exit !(v ~ /^([0-9]+([.][0-9]*)?|[.][0-9]+)$/ && v + 0 >= 0 && v + 0 <= 1) }' 2>/dev/null; then
    echo "[env] $key must be a number between 0 and 1 when set" >&2
    failed=1
  fi
}

require_int_min_if_set() {
  local key="$1"
  local min="$2"
  local value
  value=$(read_env_value "$key" || true)
  if [ -z "$value" ]; then
    return 0
  fi
  case "$value" in
    *[!0-9]*)
      echo "[env] $key must be an integer >= $min when set" >&2
      failed=1
      return
      ;;
  esac
  if [ "${#value}" -gt 9 ] || [ "$((10#$value))" -lt "$min" ]; then
    echo "[env] $key must be an integer between $min and 999999999 when set" >&2
    failed=1
  fi
}

typesafe_surface_flags=(
  TYPESAFE__WRITINGGUARDENABLED
  TYPESAFE__WRITINGROUTEENABLED
  TYPESAFE__WRITINGVERIFYENABLED
  TYPESAFE__WRITINGCRITERIAENABLED
  TYPESAFE__COMPANIONRERANKENABLED
  TYPESAFE__CONVERSATIONADVISORYENABLED
  TYPESAFE__RESPONSEVERIFYENABLED
  TYPESAFE__DEVELOPMENTTRIAGEENABLED
  TYPESAFE__WRITINGGUARDENFORCED
  TYPESAFE__WRITINGOUTCOMEENABLED
  TYPESAFE__WRITINGFINDINGSENABLED
  TYPESAFE__SPEAKINGREADINESSENABLED
  TYPESAFE__SPEAKINGCROSSCHECKENABLED
)
for typesafe_bool in TYPESAFE__ENABLED "${typesafe_surface_flags[@]}"; do
  # A malformed bool (on/yes/1) makes .NET option binding throw on first use, which is not fail-soft.
  typesafe_bool_value=$(read_env_value "$typesafe_bool" || true)
  case "$(printf '%s' "$typesafe_bool_value" | tr '[:upper:]' '[:lower:]')" in
    ""|true|false) ;;
    *)
      echo "[env] $typesafe_bool must be true or false when set" >&2
      failed=1
      ;;
  esac
done
typesafe_enabled=$(read_env_value TYPESAFE__ENABLED || true)
if [ "$(printf '%s' "$typesafe_enabled" | tr '[:upper:]' '[:lower:]')" = "true" ]; then
  require_min_length TYPESAFE__APIKEY 16
else
  # Warn only: every surface flag is a no-op until the master switch is on, so
  # this is a mis-staged rollout, not a broken deploy.
  for typesafe_flag in "${typesafe_surface_flags[@]}"; do
    typesafe_flag_value=$(read_env_value "$typesafe_flag" || true)
    if [ "$(printf '%s' "$typesafe_flag_value" | tr '[:upper:]' '[:lower:]')" = "true" ]; then
      echo "[env] WARNING: $typesafe_flag=true has no effect while TYPESAFE__ENABLED is not true" >&2
    fi
  done
fi
# The six thresholds are probabilities; the per-token price is a USD fraction
# (default 0.000000042), so anything above 1 is a typo there too.
for typesafe_fraction in \
  TYPESAFE__RESPONSECONFIDENCETHRESHOLD \
  TYPESAFE__DEVELOPMENTCONFIDENCETHRESHOLD \
  TYPESAFE__GUARDBLOCKTHRESHOLD \
  TYPESAFE__GUARDREVIEWTHRESHOLD \
  TYPESAFE__ROUTECONFIDENCETHRESHOLD \
  TYPESAFE__VERIFYCONFIDENCETHRESHOLD \
  TYPESAFE__OUTCOMECONFIDENCETHRESHOLD \
  TYPESAFE__CROSSCHECKDIVERGENCETHRESHOLD \
  TYPESAFE__CROSSCHECKCONFIDENCETHRESHOLD \
  TYPESAFE__READINESSFLAGTHRESHOLD \
  TYPESAFE__COSTPERINPUTTOKENUSD; do
  require_unit_interval_if_set "$typesafe_fraction"
done
require_int_min_if_set TYPESAFE__TIMEOUTSECONDS 1
require_int_min_if_set TYPESAFE__MAXRETRIES 0
require_int_min_if_set TYPESAFE__VERIFYMAXFINDINGSPERCALL 1
require_int_min_if_set TYPESAFE__BREAKERFAILURETHRESHOLD 1
require_int_min_if_set TYPESAFE__BREAKERCOOLDOWNSECONDS 1

# GEPA placement engine connection is OPTIONAL in the env file (same owner
# policy as live voice / LiveKit / TypeSafe): when both keys are empty the
# placement proxy fails closed with 503 placement_engine_not_configured and
# unrelated deploys are never blocked. When EITHER key IS configured, BOTH are
# validated in full — a half-configured engine connection would flip the
# runtime placement flag on without a working engine behind it.
gepa_url=$(read_env_value GEPA_ENGINE_BASE_URL || true)
gepa_token=$(read_env_value GEPA_SERVICE_TOKEN || true)
if [ -n "$gepa_url" ] || [ -n "$gepa_token" ]; then
  case "$gepa_url" in
    http://*|https://*) ;;
    "")
      echo "[env] GEPA_ENGINE_BASE_URL is required when GEPA_SERVICE_TOKEN is set" >&2
      failed=1
      ;;
    *)
      echo "[env] GEPA_ENGINE_BASE_URL must be an http(s):// URL" >&2
      failed=1
      ;;
  esac
  if [ -z "$gepa_token" ]; then
    echo "[env] GEPA_SERVICE_TOKEN is required when GEPA_ENGINE_BASE_URL is set" >&2
    failed=1
  else
    require_min_length GEPA_SERVICE_TOKEN 16
  fi
fi

# Owner Agent Console (owner directive 2026-09-27) is OPTIONAL. When enabled, the
# API slots and the oet-agent-console stack share these secrets. They must be
# HEX (`openssl rand -hex 32` for the tokens, `openssl rand -hex 24` for the DB
# password): hex can never contain the placeholder / mock / stub / noop markers
# scanned above and by mock-stub-scan.sh, and the DB password is embedded in a
# postgres:// URL without escaping.
require_hex_value() {
  local key="$1"
  local value
  value=$(read_env_value "$key" || true)
  case "$value" in
    *[!0-9A-Fa-f]*)
      echo "[env] $key must be hexadecimal (generate with openssl rand -hex)" >&2
      failed=1
      ;;
  esac
}
owner_agent_enabled=$(read_env_value OWNER_AGENT__ENABLED || true)
if [ "$(printf '%s' "$owner_agent_enabled" | tr '[:upper:]' '[:lower:]')" = "true" ]; then
  require_min_length OWNER_AGENT__INTERNALTOKEN 32
  require_min_length OWNER_AGENT__PROXYTOKEN 32
  require_min_length OWNER_AGENT__DBPASSWORD 24
  require_hex_value OWNER_AGENT__INTERNALTOKEN
  require_hex_value OWNER_AGENT__PROXYTOKEN
  require_hex_value OWNER_AGENT__DBPASSWORD
  if [ "$(read_env_value OWNER_AGENT__INTERNALTOKEN || true)" = "$(read_env_value OWNER_AGENT__PROXYTOKEN || true)" ]; then
    echo "[env] OWNER_AGENT__INTERNALTOKEN and OWNER_AGENT__PROXYTOKEN must be different secrets" >&2
    failed=1
  fi
  owner_agent_ids=$(read_env_value OWNER_AGENT__OWNERACCOUNTIDS || true)
  if [ -z "$owner_agent_ids" ]; then
    echo "[env] OWNER_AGENT__OWNERACCOUNTIDS is required when OWNER_AGENT__ENABLED=true" >&2
    failed=1
  elif ! printf '%s' "$owner_agent_ids" | grep -Eq '^[A-Za-z0-9_-]{1,64}(,[A-Za-z0-9_-]{1,64})*$'; then
    echo "[env] OWNER_AGENT__OWNERACCOUNTIDS must be a comma-separated list of auth account ids" >&2
    failed=1
  fi
  owner_agent_base_url=$(read_env_value OWNER_AGENT__BASEURL || true)
  if [ -n "$owner_agent_base_url" ] && ! printf '%s' "$owner_agent_base_url" | grep -Eq '^http://oet-agent-console(:[0-9]{1,5})?/?$'; then
    # The API sends the internal token to this URL; it must stay the internal sidecar.
    echo "[env] OWNER_AGENT__BASEURL must be http://oet-agent-console:<port> (internal network only)" >&2
    failed=1
  fi
fi

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
