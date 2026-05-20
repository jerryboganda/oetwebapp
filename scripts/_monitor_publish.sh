#!/bin/bash
sleep 90
echo === TAIL ===
tail -60 /tmp/generate-listening-live.log
echo
echo === LISTENING COUNT ===
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
SELECT "SubtestCode", "Status", count(*) FROM "ContentPapers"
  WHERE "SubtestCode" IN ('listening','reading','writing','speaking')
  GROUP BY 1,2 ORDER BY 1,2;
SQL
echo
echo === ALIVE ===
ps -ef | grep -E 'generate-(listening|reading)' | grep -v grep
