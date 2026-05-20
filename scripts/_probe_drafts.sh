#!/usr/bin/env bash
set -uo pipefail
echo "=== counts ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -P pager=off -c 'SELECT "SubtestCode", "Status", COUNT(*) FROM "ContentPapers" GROUP BY "SubtestCode","Status" ORDER BY "SubtestCode","Status";' 2>&1
echo ""
echo "=== draft listening + asset summary ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -P pager=off -c 'SELECT cp."Title", (SELECT COUNT(*) FROM "ContentPaperAssets" WHERE "PaperId"=cp."Id") AS total, (SELECT COUNT(*) FROM "ContentPaperAssets" WHERE "PaperId"=cp."Id" AND "Role"=0) AS aud, (SELECT COUNT(*) FROM "ContentPaperAssets" WHERE "PaperId"=cp."Id" AND "Role"=2) AS scr, (SELECT COUNT(*) FROM "ContentPaperAssets" WHERE "PaperId"=cp."Id" AND "Role"=1) AS qp, (SELECT COUNT(*) FROM "ContentPaperAssets" WHERE "PaperId"=cp."Id" AND "Role"=3) AS ak FROM "ContentPapers" cp WHERE cp."SubtestCode"=$$listening$$ AND cp."Status"=0 ORDER BY cp."Title";' 2>&1
echo ""
echo "=== running orchs ==="
pgrep -af 'generate-listening|generate-reading|_finalize|_reading_wait' 2>&1 || echo "(none)"
