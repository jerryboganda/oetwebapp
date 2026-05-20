#!/usr/bin/env bash
set -u
cd /opt/oetwebapp

# Kill the skip-tts listening orch — it just adds drafts we can't publish
if pgrep -f 'generate-listening.mjs.*--skip-tts' >/dev/null; then
  pkill -f 'generate-listening.mjs.*--skip-tts'
  echo "killed generate-listening skip-tts orch"
  sleep 2
fi

# Kill any stale rwcr / reading-watch wrapper
pgrep -af 'rwcr|_rwcr' | awk '{print $1}' | while read pid; do
  [ -n "$pid" ] && kill "$pid" 2>/dev/null && echo "killed stale wrapper $pid"
done

# Make sure retry-listening-tts is alive; if not, restart with more conservative pacing
if ! pgrep -f 'retry-listening-tts.mjs' >/dev/null; then
  source /opt/oetwebapp/scripts/admin/.envrc
  nohup node scripts/admin/retry-listening-tts.mjs \
    --poll-seconds 240 \
    --paper-sleep 120 \
    --part-sleep 30 \
    --tts-retries 30 \
    > /tmp/retry-listening-tts.log 2>&1 &
  echo "restarted retry-listening-tts PID=$!"
else
  echo "retry-listening-tts already running"
fi

sleep 3
echo ''
echo '===== ALIVE DAEMONS ====='
pgrep -af 'generate-reading|generate-listening|retry-listening-tts|_finalize_when_done' | head -10
