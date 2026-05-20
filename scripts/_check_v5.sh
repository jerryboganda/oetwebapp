#!/usr/bin/env bash
set -u
echo "=== PROCS ==="
pgrep -af 'generate-(reading|listening|mocks)\.mjs' || echo "no orch procs"
echo
echo "=== FINALIZER ==="
pgrep -af '_finalize_when_done\.sh' || echo "finalizer not running"
echo
echo "=== FINALIZE LOG TAIL ==="
tail -20 /opt/oetwebapp/output/_finalize.log 2>/dev/null || echo "(no log yet)"
echo
echo "=== READING TAIL ==="
ls -1t /opt/oetwebapp/output/admin-bulk/generate-reading-*.jsonl 2>/dev/null | head -1 | xargs -r tail -5 2>/dev/null || true
echo
echo "=== LISTENING TAIL ==="
ls -1t /opt/oetwebapp/output/admin-bulk/generate-listening-*.jsonl 2>/dev/null | head -1 | xargs -r tail -3 2>/dev/null || true
echo
echo "=== DB ==="
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off <<'SQL' 2>&1
SELECT 'papers|' || "SubtestCode" || '|' || "Status" || '|' || COUNT(*) FROM "ContentPapers" GROUP BY "SubtestCode","Status" ORDER BY 2,3;
SELECT 'mocks|' || "Status" || '|' || COUNT(*) FROM "MockBundles" GROUP BY "Status" ORDER BY 2;
SELECT 'crossprof|' || "SubtestCode" || '|' || "AppliesToAllProfessions" || '|' || COUNT(*) FROM "ContentPapers" WHERE "Status"=4 GROUP BY "SubtestCode","AppliesToAllProfessions" ORDER BY 2,3;
SQL
echo
echo "=== DUMP FILE ==="
ls -lh /opt/oetwebapp/output/oet_learner-final.dump 2>/dev/null || echo "(no dump yet)"
