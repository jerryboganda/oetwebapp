#!/bin/bash
set -eo pipefail
PSQL="docker exec oet-postgres psql -U oet_learner -d oet_learner -tA"

echo "=== BEFORE: AdminUploadSessions states ==="
$PSQL -c 'SELECT "State", COUNT(*) FROM "AdminUploadSessions" GROUP BY "State" ORDER BY "State";'

echo
echo "=== BEFORE: ContentPapers status counts (listening/reading) ==="
$PSQL -c $'SELECT "SubtestCode", "Status", COUNT(*) FROM "ContentPapers" WHERE "SubtestCode" IN (\'listening\',\'reading\') GROUP BY "SubtestCode","Status" ORDER BY "SubtestCode","Status";'

echo
echo "=== Abort stale Uploading (State=2 from >5min ago) ==="
$PSQL -c $'UPDATE "AdminUploadSessions" SET "State"=4 WHERE "State"=2 AND "CreatedAt" < NOW() - interval \'5 minute\';'

echo
echo "=== Show recent Draft listening/reading ContentPapers (no primary asset) ==="
$PSQL -c $'SELECT cp."Id", cp."SubtestCode", cp."Slug", cp."Status", cp."CreatedAt" FROM "ContentPapers" cp WHERE cp."SubtestCode" IN (\'listening\',\'reading\') AND cp."Status"=0 ORDER BY cp."CreatedAt" DESC LIMIT 20;'

echo
echo "=== After ==="
$PSQL -c 'SELECT "State", COUNT(*) FROM "AdminUploadSessions" GROUP BY "State" ORDER BY "State";'

echo
echo "=== Check still-running orchestrator processes ==="
ps -ef | grep -E 'node.*scripts/admin' | grep -v grep || echo "(none)"
