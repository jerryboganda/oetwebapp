#!/usr/bin/env bash
set -e
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -P pager=off <<'SQL' 2>&1
\echo === BEFORE ===
SELECT "SubtestCode", "AppliesToAllProfessions", COUNT(*) AS c
  FROM "ContentPapers" WHERE "Status"=4 GROUP BY 1,2 ORDER BY 1,2;
UPDATE "ContentPapers" SET "AppliesToAllProfessions"=true, "UpdatedAt"=NOW()
  WHERE "Status"=4 AND "AppliesToAllProfessions"=false;
\echo === AFTER ===
SELECT "SubtestCode", "AppliesToAllProfessions", COUNT(*) AS c
  FROM "ContentPapers" WHERE "Status"=4 GROUP BY 1,2 ORDER BY 1,2;
SQL
