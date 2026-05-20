#!/bin/bash
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
SELECT "SubtestCode" AS subtest, "Status" AS status, count(*) AS n
  FROM "ContentPapers" GROUP BY 1,2 ORDER BY 1,2;
SQL
