#!/bin/bash
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
\echo === ConversationTemplates
SELECT "Status", count(*) FROM "ConversationTemplates" GROUP BY 1;
\echo === GrammarLessons
\d "GrammarLessons"
SELECT "Status", count(*) FROM "GrammarLessons" GROUP BY 1;
\echo === PronunciationDrills
\d "PronunciationDrills"
SELECT "Status", count(*) FROM "PronunciationDrills" GROUP BY 1;
\echo === Speaking sample (Id only)
SELECT "Id", LEFT("Title",60) FROM "ContentPapers" WHERE "SubtestCode"='speaking' AND "Status"=0 LIMIT 3;
SQL
