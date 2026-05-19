#!/usr/bin/env bash
# Show last few lines of each generator log + alive/dead status
cd /tmp || exit 2

LOGS=(
  vocab-live.log
  generate-conversation-live.log
  generate-grammar-live.log
  generate-pronunciation-live.log
  generate-speaking-live.log
  generate-reading-live.log
  generate-mocks-live.log
  generate-listening-live.log
)

echo "═══════════════════════════════════════════════════════════════════"
echo "  Live status @ $(date -Iseconds)"
echo "═══════════════════════════════════════════════════════════════════"
for f in "${LOGS[@]}"; do
  base="${f%-live.log}"
  base="${base#generate-}"
  if [[ -f "$f" ]]; then
    line=$(grep -E '^\[[0-9]+/' "$f" | tail -1)
    if [[ -z "$line" ]]; then
      line=$(tail -1 "$f")
    fi
    finished=$(grep -E 'finished in [0-9]' "$f" | tail -1)
    echo "── ${base}"
    echo "   ${line:-(no progress yet)}"
    if [[ -n "$finished" ]]; then
      echo "   ✓ ${finished}"
    fi
  else
    echo "── ${base}: (no log)"
  fi
done

echo ""
echo "=== alive processes ==="
pgrep -af "node scripts/admin/" | sed 's|/opt/oetwebapp/||g' | sort
