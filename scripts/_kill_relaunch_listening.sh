#!/usr/bin/env bash
set -uo pipefail
echo "=== killing failed listening orch ==="
pkill -f 'generate-listening\.mjs' || true
sleep 3
echo ""
echo "=== sourcing envrc + relaunching ==="
cd /opt/oetwebapp
set +u
source /opt/oetwebapp/scripts/admin/.envrc
set -u
nohup node scripts/admin/generate-listening.mjs --count 120 --resume --skip-tts \
  > /tmp/generate-listening-skiptts.log 2>&1 &
NEWPID=$!
disown
echo "new listening orch PID=$NEWPID"
sleep 8
echo ""
echo "=== ps ==="
pgrep -af 'generate-listening\.mjs' | head -5
echo ""
echo "=== last 25 lines ==="
tail -25 /tmp/generate-listening-skiptts.log 2>/dev/null || echo "no log yet"
