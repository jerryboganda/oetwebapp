#!/usr/bin/env bash
set -e
cd /opt/oetwebapp

# 1. Kill listening orchestrator (it has stale maxTokens=8000 cached)
echo "=== killing listening pid 2346246 ==="
kill 2346246 2>/dev/null || echo "(already dead)"
sleep 2
kill -9 2346246 2>/dev/null || true

# 2. Wipe any draft listening/reading papers created in the last 4 hours
echo "=== cleaning recent drafts ==="
PGPASSWORD="${PGPASS:?set PGPASS}" docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
DELETE FROM "ContentPapers"
WHERE "SubtestCode" IN ('listening','reading')
  AND "Status" = 0
  AND "CreatedAt" > now() - interval '6 hours';
SQL

# 3. Reset manifests
echo "=== resetting manifests ==="
mkdir -p output/admin-bulk
echo '{"papers":[]}' > output/admin-bulk/generate-listening-manifest.json
echo '{"papers":[]}' > output/admin-bulk/generate-reading-manifest.json

# 4. Relaunch both
echo "=== launching listening ==="
nohup bash scripts/admin/run-bulk.sh generate-listening.mjs > /tmp/generate-listening-live.log 2>&1 </dev/null & disown
LPID=$!
sleep 2
echo "  listening pid=$LPID"

echo "=== launching reading ==="
nohup bash scripts/admin/run-bulk.sh generate-reading.mjs > /tmp/generate-reading-live.log 2>&1 </dev/null & disown
RPID=$!
sleep 2
echo "  reading pid=$RPID"

sleep 5
echo "=== alive check ==="
ps -p $LPID -o pid,etime,cmd 2>&1 || echo "listening DIED"
ps -p $RPID -o pid,etime,cmd 2>&1 || echo "reading DIED"
