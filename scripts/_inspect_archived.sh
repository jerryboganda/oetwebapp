#!/usr/bin/env bash
set -e
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -c "SELECT cp."Id" || '|' || cp."Title" || '|' || COALESCE(p."Code",'?') || '|' || COALESCE(cp."Difficulty",'?') || '|' || COALESCE(cp."ExamCode",'?') FROM "ContentPapers" cp LEFT JOIN "Professions" p ON p."Id" = cp."ProfessionId" WHERE cp."SubtestCode"='listening' AND cp."Status"=6 ORDER BY cp."CreatedAt";"
