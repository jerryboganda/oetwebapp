#!/usr/bin/env bash
sleep 360
echo "=== LISTENING tail ==="
tail -40 /tmp/generate-listening-live.log
echo
echo "=== READING tail ==="
tail -15 /tmp/generate-reading-live.log
echo
echo "=== VOCAB tail ==="
tail -5 /tmp/publish-vocab-live.log
echo
echo "=== STATUS ==="
ps -eo pid,etime,cmd | grep -E 'node scripts/admin/' | grep -v grep
echo
echo "=== DB ==="
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
select "SubtestCode", "Status", count(*) from "ContentPapers"
where "CreatedAt" > now() - interval '2 hours' group by 1,2 order by 1,2;
SQL
