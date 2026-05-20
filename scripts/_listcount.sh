#!/usr/bin/env bash
set -e
docker exec oet-postgres psql -U oet_learner -d oet_learner -t -A -F'|' -c "
SELECT \"ProfessionCode\", \"Status\", COUNT(*)
FROM \"ContentPapers\"
WHERE \"SkillSubtest\" = 'listening'
GROUP BY 1,2
ORDER BY 1,2;"
echo '---reading---'
docker exec oet-postgres psql -U oet_learner -d oet_learner -t -A -F'|' -c "
SELECT \"ProfessionCode\", \"Status\", COUNT(*)
FROM \"ContentPapers\"
WHERE \"SkillSubtest\" = 'reading'
GROUP BY 1,2
ORDER BY 1,2;"
