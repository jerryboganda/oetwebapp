#!/usr/bin/env bash
# Autonomous finalizer v2 — checks DB state, kills polling daemons, then runs sync.
# Triggers when: reading orch dead AND listening_drafts == 0 (kills retry-TTS daemon).

set -u
LOG=/opt/oetwebapp/output/_finalize.log
DUMP=/opt/oetwebapp/output/oet_learner-final.dump

while true; do
  R=$(pgrep -f 'generate-reading.mjs' | head -1)
  L=$(pgrep -f 'retry-listening-tts.mjs' | head -1)
  DRAFTS=$(docker exec -i oet-postgres psql -U oet_learner -d oet_learner -tA -P pager=off -c "SELECT count(*) FROM \"ContentPapers\" WHERE \"SubtestCode\"='listening' AND \"Status\"=0;" 2>/dev/null | tr -d '[:space:]')
  TS=$(date '+%F %T')
  echo "[$TS] reading_pid=${R:-NONE} listening_pid=${L:-NONE} drafts=${DRAFTS:-?}" >> "$LOG"
  if [ -z "$R" ] && [ "${DRAFTS:-1}" = "0" ]; then
    if [ -n "$L" ]; then
      echo "[$TS] 0 drafts left — killing retry-TTS daemon pid=$L" >> "$LOG"
      kill "$L" 2>>"$LOG" || true
      sleep 5
    fi
    echo "[$TS] READY - running finalizer" >> "$LOG"
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
