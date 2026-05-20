#!/usr/bin/env bash
PID="$1"
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "
SELECT \"QuestionNumber\" AS n, LEFT(\"Stem\",70) AS stem, LEFT(\"CorrectAnswer\",30) AS ca, \"PartCode\"
FROM \"ListeningQuestions\"
WHERE \"PaperId\"='${PID}'
ORDER BY \"QuestionNumber\" LIMIT 6;"
echo '---count---'
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "
SELECT COUNT(*) total, COUNT(*) FILTER (WHERE LENGTH(COALESCE(\"Stem\",''))=0) blank_stems, COUNT(*) FILTER (WHERE LENGTH(COALESCE(\"CorrectAnswer\",''))=0) blank_answers
FROM \"ListeningQuestions\" WHERE \"PaperId\"='${PID}';"
echo '---json sample---'
docker exec oet-postgres psql -U oet_learner -d oet_learner -t -A -c "
SELECT jsonb_extract_path_text((\"ExtractedTextJson\"::jsonb), 'listeningQuestions', '0')
FROM \"ContentPapers\" WHERE \"Id\"='${PID}'::uuid;" | head -c 500
echo
