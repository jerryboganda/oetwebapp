#!/usr/bin/env bash
# Launch all remaining content generators in parallel.
# Vocab is assumed to already be running in the background.
set -u
cd /opt/oetwebapp || exit 2

mkdir -p /tmp

SCRIPTS=(
  generate-conversation.mjs
  generate-grammar.mjs
  generate-pronunciation.mjs
  generate-speaking.mjs
  generate-reading.mjs
  generate-mocks.mjs
  generate-listening.mjs
)

for s in "${SCRIPTS[@]}"; do
  base="${s%.mjs}"
  logname="/tmp/${base}-live.log"
  # If already running, skip.
  if pgrep -f "node scripts/admin/${s}" >/dev/null 2>&1 ; then
    echo "SKIP  ${s} (already running)"
    continue
  fi
  nohup bash scripts/admin/run-bulk.sh "$s" > "$logname" 2>&1 &
  disown
  pid=$!
  echo "LAUNCH ${s}  pid=${pid}  log=${logname}"
  sleep 0.4
done

sleep 3
echo ""
echo "=== Running processes ==="
pgrep -af "node scripts/admin/" | sort
