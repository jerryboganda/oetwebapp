#!/bin/bash
PID="${1:-e223ca4919cd4e85af2c22570ad7be73}"
docker exec oet-postgres psql -U oet_learner oet_learner -tA <<SQL
SELECT q->>'Number' AS num
FROM "ContentPapers", jsonb_array_elements(COALESCE("ExtractedTextJson"->'listeningQuestions','[]'::jsonb)) q
WHERE "Id"='$PID'
ORDER BY (q->>'Number')::int;
SQL
echo --COUNTS--
docker exec oet-postgres psql -U oet_learner oet_learner -tA <<SQL
SELECT q->>'Number' AS num, COUNT(*)
FROM "ContentPapers", jsonb_array_elements(COALESCE("ExtractedTextJson"->'listeningQuestions','[]'::jsonb)) q
WHERE "Id"='$PID'
GROUP BY q->>'Number'
HAVING COUNT(*) > 1;
SQL
echo --TOTAL--
docker exec oet-postgres psql -U oet_learner oet_learner -tA <<SQL
SELECT jsonb_array_length(COALESCE("ExtractedTextJson"->'listeningQuestions','[]'::jsonb))
FROM "ContentPapers" WHERE "Id"='$PID';
SQL
