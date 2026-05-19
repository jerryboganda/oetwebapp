#!/bin/bash
# Bulk content backfill launcher — sets all required env vars then runs the requested script.
# Usage: bash /opt/oetwebapp/scripts/admin/run-bulk.sh <script-name.mjs> [args...]
# Example: bash /opt/oetwebapp/scripts/admin/run-bulk.sh publish-vocab.mjs --dry-run --limit 5
set -euo pipefail
cd /opt/oetwebapp

# Load secrets from gitignored env file if present (production deploys put
# secrets in /opt/oetwebapp/scripts/admin/.envrc, never committed to git).
if [ -f scripts/admin/.envrc ]; then
  # shellcheck disable=SC1091
  source scripts/admin/.envrc
fi

export API_BASE="${API_BASE:?API_BASE env var required}"
export ADMIN_EMAIL="${ADMIN_EMAIL:?ADMIN_EMAIL env var required}"
export ADMIN_PASSWORD="${ADMIN_PASSWORD:?ADMIN_PASSWORD env var required}"
export AI__ApiKey="${AI__ApiKey:?AI__ApiKey env var required (DigitalOcean inference token)}"
export AI__BaseUrl='https://inference.do-ai.run/v1'
export AI__ChatModel='anthropic-claude-opus-4.7'
export AI__TtsModel='qwen3-tts-voicedesign'
export AI__TtsBaseUrl='https://inference.do-ai.run/v1'

# ElevenLabs is the PRIMARY TTS provider. DO Qwen3-TTS remains as automatic
# fallback inside _lib.mjs when the key is unset or the call fails. The key is
# NOT required (no `:?`) so existing DO-only environments keep working.
export ELEVENLABS__ApiKey="${ELEVENLABS__ApiKey:-${ELEVENLABS_API_KEY:-}}"
export ELEVENLABS__BaseUrl="${ELEVENLABS__BaseUrl:-https://api.elevenlabs.io/v1}"
export ELEVENLABS__Model="${ELEVENLABS__Model:-eleven_multilingual_v2}"
export ELEVENLABS__DefaultVoiceId="${ELEVENLABS__DefaultVoiceId:-EXAVITQu4vr4xnSDxMaL}"
export ELEVENLABS__VoiceMaleId="${ELEVENLABS__VoiceMaleId:-pNInz6obpgDQGcFmaJgB}"
export ELEVENLABS__VoiceFemaleId="${ELEVENLABS__VoiceFemaleId:-EXAVITQu4vr4xnSDxMaL}"

if [ "${RUN_BULK_CONFIRM:-}" != "live-admin-bulk" ]; then
  echo "RUN_BULK_CONFIRM=live-admin-bulk is required before running admin bulk scripts."
  exit 2
fi

SCRIPT="${1:-}"
if [ -z "$SCRIPT" ]; then
  echo "usage: $0 <script.mjs> [args...]"
  exit 2
fi
shift

exec node "scripts/admin/$SCRIPT" "$@"
