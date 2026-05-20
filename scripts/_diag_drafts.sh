#!/usr/bin/env bash
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -F'|' -P pager=off <<'SQL'
SELECT cp."Id",
       sum(case when cpa."Role"=2 then 1 else 0 end) AS scripts,
       sum(case when cpa."Role"=0 then 1 else 0 end) AS audios,
       (cp."ExtractedTextJson" IS NOT NULL) AS has_extract,
       cp."Title"
FROM "ContentPapers" cp
LEFT JOIN "ContentPaperAssets" cpa ON cpa."PaperId"=cp."Id"
WHERE cp."SubtestCode"='listening' AND cp."Status"=0
GROUP BY cp."Id", cp."ExtractedTextJson", cp."Title"
ORDER BY scripts, has_extract;
SQL
