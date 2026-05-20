#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp
set +u; source scripts/admin/.envrc; set -u

pkill -f generate-reading.mjs 2>/dev/null || true
sleep 2

nohup node scripts/admin/generate-reading.mjs --count 110 --resume \
  > /tmp/generate-reading-live.log 2>&1 &
RPID=$!
disown
echo "READING_PID=$RPID"

sleep 15
echo ""
echo "--- alive? ---"
pgrep -af generate-reading.mjs || echo NOT_ALIVE
echo ""
echo "--- log tail ---"
tail -25 /tmp/generate-reading-live.log
