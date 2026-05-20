#!/bin/bash
# Dry-run every generator script and tail the outcome. Args optional.
set -uo pipefail
cd /opt/oetwebapp

SCRIPTS=(
  generate-conversation.mjs
  generate-grammar.mjs
  generate-pronunciation.mjs
  generate-speaking.mjs
  generate-reading.mjs
  generate-mocks.mjs
)

for s in "${SCRIPTS[@]}"; do
  echo "═══════════════════════════════════════════════════════════════════"
  echo "DRY-RUN: $s"
  echo "───────────────────────────────────────────────────────────────────"
  bash scripts/admin/run-bulk.sh "$s" --dry-run --count 1 --limit 1 2>&1 | tail -20 || echo "EXIT=$?"
done

# Listening separately with --skip-tts
echo "═══════════════════════════════════════════════════════════════════"
echo "DRY-RUN: generate-listening.mjs --skip-tts"
echo "───────────────────────────────────────────────────────────────────"
bash scripts/admin/run-bulk.sh generate-listening.mjs --dry-run --count 1 --limit 1 --skip-tts 2>&1 | tail -20 || echo "EXIT=$?"
