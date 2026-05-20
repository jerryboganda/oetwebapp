#!/usr/bin/env bash
set -e
cd /opt/oetwebapp

# Kill any running listening orchestrator
for pid in $(pgrep -f 'generate-listening.mjs' || true); do
  echo "killing listening pid $pid"
  kill -9 "$pid" || true
done
sleep 2

# Delete recent (<6h) Draft listening shells with no audio assets (failed attempts)
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -v ON_ERROR_STOP=1 <<'SQL'
DELETE FROM "ContentPaperAssets" WHERE "ContentPaperId" IN (
  SELECT "Id" FROM "ContentPapers"
  WHERE "SubtestCode" = 'listening' AND "Status" = 0
    AND "CreatedAt" > NOW() - INTERVAL '6 hours'
);
DELETE FROM "ListeningExtractionDrafts" WHERE "ContentPaperId" IN (
  SELECT "Id" FROM "ContentPapers"
  WHERE "SubtestCode" = 'listening' AND "Status" = 0
    AND "CreatedAt" > NOW() - INTERVAL '6 hours'
);
DELETE FROM "ContentPapers"
WHERE "SubtestCode" = 'listening' AND "Status" = 0
  AND "CreatedAt" > NOW() - INTERVAL '6 hours';
SELECT 'remaining listening papers' AS what,
       "Status", COUNT(*) FROM "ContentPapers"
  WHERE "SubtestCode" = 'listening' GROUP BY "Status";
SQL

# Reset manifest
mkdir -p output/admin-bulk
echo '{"papers":[]}' > output/admin-bulk/generate-listening-manifest.json

# Relaunch
cd /opt/oetwebapp
nohup bash scripts/admin/run-bulk.sh generate-listening.mjs > /tmp/generate-listening-live.log 2>&1 </dev/null & disown
sleep 3
echo "new listening pid: $(pgrep -f 'generate-listening.mjs' || echo NONE)"
tail -n 20 /tmp/generate-listening-live.log || true

