#!/usr/bin/env bash
echo "=== READING PID 2652714 ==="
if ps -p 2652714 > /dev/null 2>&1; then ps -p 2652714 -o pid,etime,pcpu,pmem,cmd | tail -1; else echo "DEAD"; fi
echo "=== LISTENING PID 3267561 ==="
if ps -p 3267561 > /dev/null 2>&1; then ps -p 3267561 -o pid,etime,pcpu,pmem,cmd | tail -1; else echo "DEAD"; fi
echo
echo "=== DB STATUS ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -t -c "SELECT \"SubtestCode\",\"Status\",COUNT(*) FROM \"ContentPapers\" GROUP BY 1,2 ORDER BY 1,2"
echo
echo "=== LISTENING ORCH TAIL (looking for difficulty fallback) ==="
tail -n 40 /tmp/generate-listening-live.log 2>/dev/null | grep -E "difficulty fallback|published|FAIL|error|\[[0-9]+/120" | tail -20
echo
echo "=== READING ORCH TAIL ==="
tail -n 30 /tmp/generate-reading-live.log 2>/dev/null | grep -E "published|FAIL|\[[0-9]+/110" | tail -10
