#!/bin/bash
echo === LISTENING LOG TAIL ===
tail -30 /tmp/generate-listening-live.log
echo
echo === LISTENING DB ===
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
SELECT "SubtestCode", "Status", count(*) FROM "ContentPapers"
  WHERE "SubtestCode" IN ('listening','reading','writing','speaking')
  GROUP BY 1,2 ORDER BY 1,2;
\echo --- speaking paper assets ---
SELECT cp."Id" pid, count(cpa."Id") asset_count
  FROM "ContentPapers" cp
  LEFT JOIN "ContentPaperAssets" cpa ON cpa."ContentPaperId"=cp."Id"
  WHERE cp."SubtestCode"='speaking'
  GROUP BY cp."Id" ORDER BY asset_count LIMIT 5;
SQL
echo
echo === ALIVE ===
ps -ef | grep -E 'generate-(listening|reading)' | grep -v grep
