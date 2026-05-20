#!/bin/bash
PID=e223ca4919cd4e85af2c22570ad7be73
PSQL='docker exec oet-postgres psql -U oet_learner oet_learner -c'
$PSQL "SELECT \"Stem\", \"CorrectAnswerJson\" FROM \"ListeningQuestions\" WHERE \"PaperId\"='$PID' ORDER BY \"QuestionNumber\" LIMIT 3;"
echo --COUNT--
$PSQL "SELECT COUNT(*) total, SUM(CASE WHEN COALESCE(\"Stem\",'')='' THEN 1 ELSE 0 END) blank_stems, SUM(CASE WHEN COALESCE(\"CorrectAnswerJson\",'')='' OR \"CorrectAnswerJson\"='\"\"' THEN 1 ELSE 0 END) blank_ans FROM \"ListeningQuestions\" WHERE \"PaperId\"='$PID';"
echo --PAPER--
$PSQL "SELECT \"SourceProvenance\" IS NULL OR \"SourceProvenance\"='' AS prov_empty FROM \"ContentPapers\" WHERE \"Id\"='$PID';"
echo --JSONHEAD--
$PSQL "SELECT LEFT(\"ExtractedTextJson\"::text, 600) FROM \"ContentPapers\" WHERE \"Id\"='$PID';"
