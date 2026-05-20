#!/usr/bin/env bash
set -e
echo "=== ORCH PIDS ==="
for p in 2652714 3267561 3305060; do
  if ps -p $p > /dev/null 2>&1; then
    ps -p $p -o pid=,etime=,cmd= | head -1
  else
    echo "$p DEAD"
  fi
done

echo ""
echo "=== DB STATUS ==="
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -P pager=off -At <<'SQL' 2>&1
SELECT 'paper|' || "SubtestCode" || '|' || "Status" || '|' || COUNT(*) FROM "ContentPapers" GROUP BY 2,3 ORDER BY 2,3;
SELECT 'bundle|' || "Status" || '|' || COUNT(*) FROM "MockBundles" GROUP BY 2 ORDER BY 2;
SQL

echo ""
echo "=== MOCKS TAIL ==="
tail -n 15 /tmp/generate-mocks-live.log

echo ""
echo "=== READING TAIL ==="
tail -n 5 /tmp/generate-reading-live.log 2>/dev/null || echo "(no log)"

echo ""
echo "=== LISTENING TAIL ==="
tail -n 5 /tmp/generate-listening-live.log 2>/dev/null || echo "(no log)"
