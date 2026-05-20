#!/bin/bash
echo === Draft listening papers ===
docker exec oet-postgres psql -U oet_learner oet_learner -c "
SELECT \"Id\", \"Title\", \"ExtractedTextJson\" IS NULL AS isnull,
       COALESCE(length(\"ExtractedTextJson\"::text), 0) AS json_len
FROM \"ContentPapers\"
WHERE \"SubtestCode\"='listening' AND \"Status\"=0
ORDER BY \"CreatedAt\" DESC
LIMIT 10;
"
