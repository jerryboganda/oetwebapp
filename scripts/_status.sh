#!/usr/bin/env bash
echo "=== PROCS ==="
pgrep -af 'node scripts/admin' || echo "(none)"
echo
echo "=== LISTENING (last 25 lines) ==="
tail -25 /tmp/generate-listening-live.log 2>/dev/null || echo "(no log)"
echo
echo "=== READING (last 20 lines) ==="
tail -20 /tmp/generate-reading-live.log 2>/dev/null || echo "(no log)"
echo
echo "=== VOCAB (last 12 lines) ==="
tail -12 /tmp/publish-vocab-live.log 2>/dev/null || tail -12 /tmp/vocab-live.log 2>/dev/null || echo "(no log)"
echo
echo "=== DB COUNTS ==="
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
SELECT "SubtestCode" AS subtest, "Status" AS status, count(*) AS n
  FROM "ContentPapers" GROUP BY 1,2 ORDER BY 1,2;
SELECT 'vocab-entries' AS label, count(*) FROM "VocabularyEntries";
SQL
