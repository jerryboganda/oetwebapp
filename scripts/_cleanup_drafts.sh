#!/bin/bash
set -eo pipefail
cd /opt/oetwebapp
PSQL="docker exec oet-postgres psql -U oet_learner -d oet_learner -tA"

echo "=== Kill stale orchestrators (keep publish-vocab running) ==="
for pid in $(ps -ef | grep -E 'node.*scripts/admin/(generate-listening|generate-reading|generate-mocks)' | grep -v grep | awk '{print $2}'); do
  echo "kill -TERM $pid"
  kill -TERM "$pid" 2>/dev/null || true
done
sleep 3
for pid in $(ps -ef | grep -E 'node.*scripts/admin/(generate-listening|generate-reading|generate-mocks)' | grep -v grep | awk '{print $2}'); do
  echo "kill -KILL $pid"
  kill -KILL "$pid" 2>/dev/null || true
done

echo
echo "=== Abort ALL remaining State=2 (uploading) ==="
$PSQL -c $'UPDATE "AdminUploadSessions" SET "State"=4 WHERE "State"=2;'

echo
echo "=== Delete Draft listening + reading papers (Status=0) and their asset rows ==="
# Get ids
IDS=$($PSQL -c $'SELECT "Id" FROM "ContentPapers" WHERE "SubtestCode" IN (\'listening\',\'reading\') AND "Status"=0;')
echo "to delete:"
echo "$IDS"
if [ -n "$IDS" ]; then
  # Delete dependent rows first via foreign key cascade or manual
  $PSQL -c $'DELETE FROM "ContentPaperAssets" WHERE "PaperId" IN (SELECT "Id" FROM "ContentPapers" WHERE "SubtestCode" IN (\'listening\',\'reading\') AND "Status"=0);'
  $PSQL -c $'DELETE FROM "ContentPaperRevisions" WHERE "PaperId" IN (SELECT "Id" FROM "ContentPapers" WHERE "SubtestCode" IN (\'listening\',\'reading\') AND "Status"=0);' 2>/dev/null || true
  $PSQL -c $'DELETE FROM "ContentPapers" WHERE "SubtestCode" IN (\'listening\',\'reading\') AND "Status"=0;'
fi

echo
echo "=== Reset manifests (only listening + reading; preserve others) ==="
for k in listening reading; do
  f="/opt/oetwebapp/output/admin-bulk/generate-$k-manifest.json"
  if [ -f "$f" ]; then
    cp "$f" "$f.bak.$(date +%s)"
    echo '{"papers":[]}' > "$f"
    echo "reset $f"
  else
    echo "no manifest $f"
  fi
done

echo
echo "=== After: AdminUploadSessions states ==="
$PSQL -c 'SELECT "State", COUNT(*) FROM "AdminUploadSessions" GROUP BY "State" ORDER BY "State";'
echo "=== After: ContentPapers (listening/reading) ==="
$PSQL -c $'SELECT "SubtestCode", "Status", COUNT(*) FROM "ContentPapers" WHERE "SubtestCode" IN (\'listening\',\'reading\') GROUP BY "SubtestCode","Status";'
