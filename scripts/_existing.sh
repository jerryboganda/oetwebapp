#!/bin/bash
PID="${1:-e223ca4919cd4e85af2c22570ad7be73}"
echo === Existing Listening rows for paper ===
docker exec oet-postgres psql -U oet_learner oet_learner -c "
SELECT 'ListeningParts' AS t, COUNT(*) FROM \"ListeningParts\" WHERE \"PaperId\"='$PID'
UNION ALL SELECT 'ListeningExtracts', COUNT(*) FROM \"ListeningExtracts\" e JOIN \"ListeningParts\" p ON e.\"ListeningPartId\"=p.\"Id\" WHERE p.\"PaperId\"='$PID'
UNION ALL SELECT 'ListeningQuestions', COUNT(*) FROM \"ListeningQuestions\" WHERE \"PaperId\"='$PID';
"
echo === Duplicate QuestionNumbers ===
docker exec oet-postgres psql -U oet_learner oet_learner -c "
SELECT \"QuestionNumber\", COUNT(*) FROM \"ListeningQuestions\" WHERE \"PaperId\"='$PID' GROUP BY \"QuestionNumber\" HAVING COUNT(*) > 1 ORDER BY \"QuestionNumber\";
"
echo === All existing Numbers ===
docker exec oet-postgres psql -U oet_learner oet_learner -c "
SELECT array_agg(\"QuestionNumber\" ORDER BY \"QuestionNumber\") FROM \"ListeningQuestions\" WHERE \"PaperId\"='$PID';
"
