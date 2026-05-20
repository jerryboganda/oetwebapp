#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp
set +u
source /opt/oetwebapp/scripts/admin/.envrc
set -u
pkill -f retry-listening-tts || true
sleep 1
LOG=/tmp/retry-listening-tts.log
nohup node scripts/admin/retry-listening-tts.mjs --poll-seconds 180 --paper-sleep 45 --part-sleep 8 --tts-retries 20 > "$LOG" 2>&1 &
PID=$!
disown $PID 2>/dev/null || true
echo "PID=$PID  LOG=$LOG"
sleep 4
ps -p $PID > /dev/null && echo "ALIVE" || echo "DEAD"
echo "--- first 20 lines ---"
head -20 "$LOG" 2>/dev/null || echo "(log empty)"
