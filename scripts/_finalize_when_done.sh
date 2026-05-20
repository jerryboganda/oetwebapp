#!/usr/bin/env bash
# Autonomous finalizer for reading + listening orchs.
# Polls every 5min; when BOTH generate-reading + generate-listening node processes
# are dead, runs the cross-profession SQL flip, the final audit, and a pg_dump.
# Outputs go to /opt/oetwebapp/output/_finalize.log

set -u
LOG=/opt/oetwebapp/output/_finalize.log
DUMP=/opt/oetwebapp/output/oet_learner-final.dump

while true; do
  R=$(pgrep -f 'generate-reading.mjs' | head -1)
  L=$(pgrep -f 'generate-listening.mjs' | head -1)
  TS=$(date '+%F %T')
  echo "[$TS] reading_pid=${R:-NONE} listening_pid=${L:-NONE}" >> "$LOG"
  if [ -z "$R" ] && [ -z "$L" ]; then
    echo "[$TS] BOTH ORCHS DEAD - running finalizer" >> "$LOG"
    break
  fi
  sleep 300
done

echo "=== AppliesToAllProfessions flip ===" >> "$LOG"
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -P pager=off >>"$LOG" 2>&1 <<'SQL'
BEGIN;
UPDATE "ContentPapers"
   SET "AppliesToAllProfessions"=true, "UpdatedAt"=NOW()
 WHERE "Status"=4 AND "AppliesToAllProfessions"=false;
COMMIT;
SELECT "SubtestCode","Status",COUNT(*) AS n,
       SUM(("AppliesToAllProfessions")::int) AS cross_prof
  FROM "ContentPapers"
 GROUP BY 1,2 ORDER BY 1,2;
SELECT 'MockBundles' AS what, "Status", COUNT(*) FROM "MockBundles" GROUP BY 2 ORDER BY 2;
SQL

echo "=== pg_dump ===" >> "$LOG"
docker exec -i oet-postgres pg_dump -U oet_learner oet_learner -Fc > "$DUMP" 2>>"$LOG"
ls -lh "$DUMP" >> "$LOG"
echo "=== DONE ===" >> "$LOG"
