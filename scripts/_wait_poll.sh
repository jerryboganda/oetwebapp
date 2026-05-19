#!/bin/bash
sleep 300
echo === TAIL ===
tail -40 /tmp/generate-listening-live.log
echo === DB ===
docker exec oet-postgres psql -U oet_learner -d oet_learner -tA -c 'SELECT "SubtestCode","Status",COUNT(*) FROM "ContentPapers" GROUP BY 1,2 ORDER BY 1,2;'
