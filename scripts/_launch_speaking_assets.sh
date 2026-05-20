#!/bin/bash
cd /opt/oetwebapp
mkdir -p /tmp
setsid nohup bash scripts/admin/run-bulk.sh generate-speaking-assets.mjs > /tmp/generate-speaking-assets-live.log 2>&1 < /dev/null &
PID=$!
disown $PID || true
sleep 1
echo "PID=$PID"
ps -p $PID -o pid,etime,cmd | tail -1
