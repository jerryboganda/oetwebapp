#!/bin/bash
set -e
docker exec oet-postgres bash -c '
psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
WITH parts AS (
  SELECT cp.\"Id\" AS paper_id, cp.\"Slug\",
         COUNT(*) FILTER (WHERE cpa.\"Role\" = 0 AND cpa.\"Part\" = '\''A1'\'') AS a1,
         COUNT(*) FILTER (WHERE cpa.\"Role\" = 0 AND cpa.\"Part\" = '\''A2'\'') AS a2,
         COUNT(*) FILTER (WHERE cpa.\"Role\" = 0 AND cpa.\"Part\" = '\''B'\'')  AS b,
         COUNT(*) FILTER (WHERE cpa.\"Role\" = 0 AND cpa.\"Part\" = '\''C1'\'') AS c1,
         COUNT(*) FILTER (WHERE cpa.\"Role\" = 0 AND cpa.\"Part\" = '\''C2'\'') AS c2
  FROM \"ContentPapers\" cp
  LEFT JOIN \"ContentPaperAssets\" cpa ON cpa.\"PaperId\" = cp.\"Id\"
  WHERE cp.\"SubtestCode\" = '\''listening'\'' AND cp.\"Status\" = 4
  GROUP BY cp.\"Id\", cp.\"Slug\"
)
SELECT
  COUNT(*)                                                       AS total_published,
  COUNT(*) FILTER (WHERE a1>0 AND a2>0 AND b>0 AND c1>0 AND c2>0) AS complete,
  COUNT(*) FILTER (WHERE NOT (a1>0 AND a2>0 AND b>0 AND c1>0 AND c2>0)) AS incomplete
FROM parts;
"
echo
psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
WITH parts AS (
  SELECT cp.\"Id\", cp.\"Slug\",
         COUNT(*) FILTER (WHERE cpa.\"Role\"=0 AND cpa.\"Part\"='\''A1'\'') a1,
         COUNT(*) FILTER (WHERE cpa.\"Role\"=0 AND cpa.\"Part\"='\''A2'\'') a2,
         COUNT(*) FILTER (WHERE cpa.\"Role\"=0 AND cpa.\"Part\"='\''B'\'')  b,
         COUNT(*) FILTER (WHERE cpa.\"Role\"=0 AND cpa.\"Part\"='\''C1'\'') c1,
         COUNT(*) FILTER (WHERE cpa.\"Role\"=0 AND cpa.\"Part\"='\''C2'\'') c2
  FROM \"ContentPapers\" cp
  LEFT JOIN \"ContentPaperAssets\" cpa ON cpa.\"PaperId\"=cp.\"Id\"
  WHERE cp.\"SubtestCode\"='\''listening'\'' AND cp.\"Status\"=4
  GROUP BY cp.\"Id\", cp.\"Slug\"
)
SELECT \"Slug\", a1, a2, b, c1, c2 FROM parts
WHERE NOT (a1>0 AND a2>0 AND b>0 AND c1>0 AND c2>0)
ORDER BY \"Slug\";
"
'
