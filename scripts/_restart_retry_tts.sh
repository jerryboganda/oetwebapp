#!/usr/bin/env bash
set -u
cd /opt/oetwebapp

# Kill existing retry-TTS and replace with conservative pacing
pkill -f 'retry-listening-tts.mjs' 2>/dev/null && echo "killed old retry-TTS" && sleep 3

source /opt/oetwebapp/scripts/admin/.envrc
nohup node scripts/admin/retry-listening-tts.mjs \
  --poll-seconds 300 \
  --paper-sleep 180 \
  --part-sleep 60 \
  --tts-retries 30 \
  > /tmp/retry-listening-tts.log 2>&1 &
NEW_PID=$!
disown
echo "new retry-listening-tts PID=$NEW_PID  (paper-sleep=180 part-sleep=60)"

sleep 3
echo ''
pgrep -af 'retry-listening-tts'
