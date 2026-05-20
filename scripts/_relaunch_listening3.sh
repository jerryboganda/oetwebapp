#!/usr/bin/env bash
set -e
cd /opt/oetwebapp

for pid in $(pgrep -f 'generate-listening.mjs' || true); do
  echo "killing listening pid $pid"
  kill -9 "$pid" || true
done
sleep 2

docker exec -i oet-postgres psql -U oet_learner -d oet_learner -v ON_ERROR_STOP=1 <<'SQL'
DELETE FROM "ContentPapers"
WHERE "SubtestCode" = 'listening' AND "Status" = 0
  AND "CreatedAt" > NOW() - INTERVAL '6 hours';
SELECT "Status", COUNT(*) FROM "ContentPapers"
  WHERE "SubtestCode" = 'listening' GROUP BY "Status" ORDER BY "Status";
SQL

mkdir -p output/admin-bulk
echo '{"papers":[]}' > output/admin-bulk/generate-listening-manifest.json

nohup bash scripts/admin/run-bulk.sh generate-listening.mjs > /tmp/generate-listening-live.log 2>&1 </dev/null & disown
sleep 4
echo "new listening pid: $(pgrep -f 'generate-listening.mjs' || echo NONE)"
echo "--- log tail ---"
tail -n 30 /tmp/generate-listening-live.log || true
