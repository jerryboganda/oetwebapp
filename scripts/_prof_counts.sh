#!/usr/bin/env bash
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -F'|' -P pager=off <<'SQL'
-- Counts per profession for listening Published
SELECT "ProfessionCode", count(*)
FROM "ContentPapers"
WHERE "SubtestCode"='listening' AND "Status"=4
GROUP BY "ProfessionCode" ORDER BY 1;
SQL
