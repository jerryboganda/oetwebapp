#!/bin/bash
echo === SCRIPTS ===
ls /opt/oetwebapp/scripts/admin/ | grep -iE 'speak|publish|conv|gram|pronun|mock'
echo
echo === SPEAKING DRAFTS SAMPLE ===
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
SELECT "Id", LEFT("Title",60) as title, "Profession" FROM "ContentPapers"
  WHERE "SubtestCode"='speaking' AND "Status"=0 LIMIT 3;
\d "ConversationTemplates"
SELECT count(*) FROM "ConversationTemplates";
SELECT count(*) FROM "GrammarLessons";
SELECT count(*) FROM "PronunciationDrills";
SQL
