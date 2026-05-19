#!/bin/bash
# Bulk content backfill launcher — sets all required env vars then runs the requested script.
# Usage: bash /opt/oetwebapp/scripts/admin/run-bulk.sh <script-name.mjs> [args...]
# Example: bash /opt/oetwebapp/scripts/admin/run-bulk.sh publish-vocab.mjs --dry-run --limit 5
set -euo pipefail
cd /opt/oetwebapp

export API_BASE='https://api.oetwithdrhesham.co.uk'
export ADMIN_EMAIL='manwara575@gmail.com'
export ADMIN_PASSWORD="${ADMIN_PASSWORD:?ADMIN_PASSWORD env var required}"
export AI__ApiKey="${AI__ApiKey:?AI__ApiKey env var required (DigitalOcean inference token)}"
export AI__BaseUrl='https://inference.do-ai.run/v1'
export AI__ChatModel='anthropic-claude-opus-4.7'
export AI__TtsModel='qwen3-tts-voicedesign'
export AI__TtsBaseUrl='https://inference.do-ai.run/v1'

SCRIPT="${1:-}"
if [ -z "$SCRIPT" ]; then
  echo "usage: $0 <script.mjs> [args...]"
  exit 2
fi
shift

exec node "scripts/admin/$SCRIPT" "$@"
