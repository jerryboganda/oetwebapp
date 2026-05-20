#!/usr/bin/env bash
# Pure-DO listening-TTS sweep (ElevenLabs quota exhausted).
set -e
cd /opt/oetwebapp
set -a
. <(grep -E '^AI__|^DIGITALOCEAN__|^OPENAI_' .env.production)
set +a
unset ELEVENLABS__APIKEY ELEVENLABS__ApiKey ELEVENLABS_API_KEY 2>/dev/null || true
LOG=/opt/oetwebapp/sweep-puredo.log
: > "$LOG"
nohup node /opt/oetwebapp/scripts/admin/retry-listening-tts.mjs \
  --paper-sleep 20 --part-sleep 3 --tts-retries 25 \
  >> "$LOG" 2>&1 &
PID=$!
echo "PID=$PID  log=$LOG"
sleep 10
echo '--- early-tail ---'
tail -25 "$LOG"
echo '--- proc ---'
ps -o pid,etime,cmd -p "$PID" 2>/dev/null || echo PROC_GONE
