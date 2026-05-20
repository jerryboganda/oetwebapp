#!/usr/bin/env bash
set -u
echo "=== DB ==="
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off <<'SQL' 2>&1
SELECT 'papers|' || "SubtestCode" || '|' || "Status" || '|' || COUNT(*) FROM "ContentPapers" GROUP BY "SubtestCode","Status" ORDER BY "SubtestCode","Status";
SELECT 'mocks|' || "Status" || '|' || COUNT(*) FROM "MockBundles" GROUP BY "Status" ORDER BY "Status";
SELECT 'crossprof|' || "SubtestCode" || '|' || "AppliesToAllProfessions" || '|' || COUNT(*) FROM "ContentPapers" WHERE "Status"=4 GROUP BY "SubtestCode","AppliesToAllProfessions" ORDER BY "SubtestCode";
SQL

echo
echo "=== READING TAIL (last 8 lines) ==="
RDG=$(ls -1t /opt/oetwebapp/output/admin-bulk/generate-reading-*.log 2>/dev/null | head -1)
[ -n "$RDG" ] && tail -8 "$RDG" || echo "no log"

echo
echo "=== LISTENING TAIL (last 5 lines) ==="
LST=$(ls -1t /opt/oetwebapp/output/admin-bulk/generate-listening-*.log 2>/dev/null | head -1)
[ -n "$LST" ] && tail -5 "$LST" || echo "no log"

echo
echo "=== /tmp logs ==="
ls -la /tmp/generate-*.log 2>/dev/null | head -5
echo "--- reading.live tail ---"
tail -5 /tmp/generate-reading-live.log 2>/dev/null || true
echo "--- listening.live tail ---"
tail -5 /tmp/generate-listening-live.log 2>/dev/null || true
