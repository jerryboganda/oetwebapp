#!/usr/bin/env bash
# Relaunch all bulk orchestrators that were killed/blocked.
cd /opt/oetwebapp

start_one() {
  local script="$1"
  local log="/tmp/${script%.mjs}-live.log"
  if pgrep -f "node scripts/admin/$script" > /dev/null; then
    echo "[skip] $script already running"
    return 0
  fi
  echo "[start] $script -> $log"
  nohup bash scripts/admin/run-bulk.sh "$script" --resume > "$log" 2>&1 </dev/null &
  disown
  sleep 1
}

start_one generate-grammar.mjs
start_one generate-reading.mjs
start_one generate-listening.mjs

sleep 4
echo
echo "=== Running orchestrators ==="
ps -eo pid,etime,cmd | grep -E 'node scripts/admin/generate-' | grep -v grep || echo "(none)"
