#!/bin/bash
PID="${1:-e223ca4919cd4e85af2c22570ad7be73}"
echo === Numbers + counts ===
docker exec oet-postgres psql -U oet_learner oet_learner -c "
SELECT q->>'Number' AS num, COUNT(*) AS cnt
FROM \"ContentPapers\", jsonb_array_elements((\"ExtractedTextJson\"::jsonb)->'listeningQuestions') q
WHERE \"Id\"='$PID'
GROUP BY q->>'Number'
ORDER BY (q->>'Number')::int;
"
echo === Total array length ===
docker exec oet-postgres psql -U oet_learner oet_learner -c "
SELECT jsonb_array_length((\"ExtractedTextJson\"::jsonb)->'listeningQuestions') AS total
FROM \"ContentPapers\" WHERE \"Id\"='$PID';
"
echo === PartCode distribution ===
docker exec oet-postgres psql -U oet_learner oet_learner -c "
SELECT q->>'PartCode' AS pc, COUNT(*) FROM \"ContentPapers\",
jsonb_array_elements((\"ExtractedTextJson\"::jsonb)->'listeningQuestions') q
WHERE \"Id\"='$PID' GROUP BY q->>'PartCode' ORDER BY 1;
"
