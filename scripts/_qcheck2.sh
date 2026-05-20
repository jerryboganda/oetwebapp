#!/usr/bin/env bash
PID="$1"
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "\d \"ListeningQuestions\"" | head -60
echo '---data---'
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "
SELECT \"QuestionNumber\", LEFT(\"Stem\",60), \"PartCode\"
FROM \"ListeningQuestions\" WHERE \"PaperId\"='${PID}' ORDER BY \"QuestionNumber\" LIMIT 6;"
echo '---json---'
docker exec oet-postgres psql -U oet_learner -d oet_learner -t -A -c "
SELECT \"ExtractedTextJson\"::jsonb -> 'listeningQuestions' -> 0
FROM \"ContentPapers\" WHERE \"Id\"='${PID}';" | head -c 800
echo
