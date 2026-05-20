#!/bin/bash
echo "=== LISTENING tail ==="
tail -40 /tmp/generate-listening-live.log
echo
echo "=== READING tail ==="
tail -40 /tmp/generate-reading-live.log
echo
echo "=== DB recent papers ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -tA -c $'SELECT "SubtestCode","Status",COUNT(*) FROM "ContentPapers" WHERE "SubtestCode" IN (\'listening\',\'reading\') AND "CreatedAt" > NOW()-interval \'10 min\' GROUP BY 1,2 ORDER BY 1,2;'
echo
echo "=== Upload sessions ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -tA -c $'SELECT "State",COUNT(*) FROM "AdminUploadSessions" WHERE "CreatedAt" > NOW()-interval \'10 min\' GROUP BY 1 ORDER BY 1;'
echo
echo "=== Recent assets ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -tA -c 'SELECT COUNT(*) FROM "MediaAssets" WHERE "CreatedAt" > NOW()-interval $$10 min$$;'
echo
echo "=== Procs ==="
ps -ef | grep -E 'node.*scripts/admin' | grep -v grep || echo "(none)"
