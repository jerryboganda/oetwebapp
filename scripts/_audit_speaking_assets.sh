#!/bin/bash
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
\d "ContentPaperAssets"
\echo --- speaking paper asset roles ---
SELECT cp."Id" pid, string_agg(cpa."Role"::text, ',') roles
  FROM "ContentPapers" cp
  LEFT JOIN "ContentPaperAssets" cpa ON cpa."PaperId"=cp."Id"
  WHERE cp."SubtestCode"='speaking'
  GROUP BY cp."Id" LIMIT 5;
SQL
