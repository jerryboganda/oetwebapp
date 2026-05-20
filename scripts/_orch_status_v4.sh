#!/usr/bin/env bash
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -P pager=off <<'SQL' 2>&1
SELECT "Status", COUNT(*) FROM "MockBundles" GROUP BY 1 ORDER BY 1;
SELECT "SubtestCode","Status",COUNT(*) FROM "ContentPapers" GROUP BY 1,2 ORDER BY 1,2;
SQL
echo === mocks proc ===
ps aux | grep -E 'generate-(mocks|reading|listening)' | grep -v grep | awk '{print $2, $10, $11, $12, $13}'
echo === latest mock failure log ===
ls -lt /opt/oetwebapp/output/admin-bulk/failures-generate-mocks-2026-05-19T04*.jsonl 2>/dev/null | head -2
