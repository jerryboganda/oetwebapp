#!/usr/bin/env bash
# Make all published writing+speaking papers appliesToAllProfessions so mocks
# (which build profession-pinned bundles) can pair them with any profession.
set -e
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -P pager=off <<'SQL' 2>&1
\echo === BEFORE ===
SELECT "SubtestCode", "AppliesToAllProfessions", COUNT(*) c
FROM "ContentPapers"
WHERE "Status"=4 AND "SubtestCode" IN ('writing','speaking')
GROUP BY 1,2 ORDER BY 1,2;

UPDATE "ContentPapers"
SET "AppliesToAllProfessions"=true, "UpdatedAt"=NOW()
WHERE "Status"=4 AND "SubtestCode" IN ('writing','speaking');

\echo === AFTER ===
SELECT "SubtestCode", "AppliesToAllProfessions", COUNT(*) c
FROM "ContentPapers"
WHERE "Status"=4 AND "SubtestCode" IN ('writing','speaking')
GROUP BY 1,2 ORDER BY 1,2;
SQL
