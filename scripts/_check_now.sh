#!/bin/bash
echo === TAIL ===
tail -50 /tmp/generate-listening-live.log
echo
echo === DB ===
docker exec oet-postgres psql -U oet_learner -d oet_learner -tA -c 'SELECT "SubtestCode","Status",COUNT(*) FROM "ContentPapers" GROUP BY 1,2 ORDER BY 1,2;'
echo
echo === PROCS ===
ps -ef | grep -E 'generate-' | grep -v grep
