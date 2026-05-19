#!/usr/bin/env bash
set -e
cd /opt/oetwebapp
# Ensure orchestrator env is loaded
[ -f scripts/admin/.envrc ] && source scripts/admin/.envrc
# Launch mocks orch detached
setsid nohup bash scripts/admin/run-bulk.sh generate-mocks.mjs --count 24 --resume > /tmp/generate-mocks-live.log 2>&1 < /dev/null &
MOCKS_PID=$!
disown $MOCKS_PID 2>/dev/null || true
sleep 3
echo "MOCKS_PID=$MOCKS_PID"
ps -p $MOCKS_PID -o pid,etime,cmd 2>/dev/null || echo "DEAD-ALREADY"
echo "--- first log lines ---"
head -n 30 /tmp/generate-mocks-live.log 2>/dev/null || echo "(no log yet)"
