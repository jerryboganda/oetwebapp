#!/usr/bin/env bash
cd /opt/oetwebapp
start_one() {
  local script="$1"
  shift
  local args="$@"
  local base="${script%.mjs}"
  local log="/tmp/${base}-live.log"
  if pgrep -f "node scripts/admin/$script" > /dev/null; then
    echo "[skip] $script already running"
    return 0
  fi
  echo "[start] $script $args -> $log"
  nohup bash scripts/admin/run-bulk.sh "$script" $args > "$log" 2>&1 </dev/null &
  disown
  sleep 1
}

start_one publish-vocab.mjs --resume
start_one generate-mocks.mjs --resume

sleep 4
echo
echo "=== All orchestrators ==="
ps -eo pid,etime,cmd | grep -E 'node scripts/admin/(generate-|publish-)' | grep -v grep || echo '(none)'
