#!/bin/bash
cd /opt/oetwebapp
# Kill any prior sweep
pkill -f retry-listening-tts.mjs 2>/dev/null || true
sleep 1
nohup node --env-file=.env.production scripts/admin/retry-listening-tts.mjs --limit 25 --part-sleep 1 > /opt/oetwebapp/sweep.log 2>&1 &
PID=$!
echo "started PID=$PID"
sleep 2
ps -p $PID > /dev/null && echo "alive" || echo "dead"
