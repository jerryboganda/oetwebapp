#!/usr/bin/env bash
PID="$1"
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "
SELECT \"QuestionNumber\" n, LEFT(\"Stem\",60) stem, LEFT(\"CorrectAnswerJson\",30) caj, \"QuestionType\" qt
FROM \"ListeningQuestions\" WHERE \"PaperId\"='${PID}' ORDER BY \"QuestionNumber\" LIMIT 6;"
echo '---blank counts---'
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "
SELECT COUNT(*) total,
 COUNT(*) FILTER (WHERE \"Stem\"='' OR \"Stem\" IS NULL) blank_stems,
 COUNT(*) FILTER (WHERE \"CorrectAnswerJson\"='' OR \"CorrectAnswerJson\" IS NULL OR \"CorrectAnswerJson\"='\"\"') blank_answers
FROM \"ListeningQuestions\" WHERE \"PaperId\"='${PID}';"
