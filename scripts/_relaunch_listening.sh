#!/bin/bash
set +e
echo "=== Kill old listening orch ==="
kill 2906441 2>/dev/null
sleep 2
ps -p 2906441 2>/dev/null && kill -9 2906441
echo "=== Launch new listening orch ==="
cd /opt/oetwebapp
setsid nohup bash scripts/admin/run-bulk.sh generate-listening.mjs --count 120 --resume > /tmp/generate-listening-live.log 2>&1 < /dev/null &
NEW=$!
echo "NEW_PID=$NEW"
sleep 5
tail -10 /tmp/generate-listening-live.log
set -u
cd /opt/oetwebapp
pkill -9 -f 'scripts/admin/generate-listening' 2>/dev/null || true
sleep 2
setsid nohup bash scripts/admin/run-bulk.sh generate-listening.mjs --count 120 --resume > /tmp/generate-listening-live.log 2>&1 < /dev/null &
disown 2>/dev/null || true
sleep 4
pgrep -af 'scripts/admin/generate-listening' || echo NOT_RUNNING
echo '--- log head ---'
head -8 /tmp/generate-listening-live.log