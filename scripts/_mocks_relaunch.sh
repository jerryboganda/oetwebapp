#!/usr/bin/env bash
# Clean failed/draft mocks from the aborted run, then relaunch with patched script.
set -e
cd /opt/oetwebapp

# Delete unpublished bundles from the prior aborted run (status<>4 or sections incomplete).
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -P pager=off <<'SQL' 2>&1
\echo === BEFORE cleanup ===
SELECT "Status", COUNT(*) FROM "MockBundles" GROUP BY 1 ORDER BY 1;

-- Delete sections + bundles that aren't Published (4). Keep the 1 that did publish.
WITH bad AS (
  SELECT "Id" FROM "MockBundles" WHERE "Status" <> 4
)
DELETE FROM "MockBundleSections" WHERE "MockBundleId" IN (SELECT "Id" FROM bad);
DELETE FROM "MockBundles" WHERE "Status" <> 4;

\echo === AFTER cleanup ===
SELECT "Status", COUNT(*) FROM "MockBundles" GROUP BY 1 ORDER BY 1;
SQL

# Source secrets and relaunch mocks (resume mode keeps the 1 already published).
source /opt/oetwebapp/scripts/admin/.envrc
nohup node /opt/oetwebapp/scripts/admin/generate-mocks.mjs --count 24 --resume \
  > /tmp/generate-mocks-live.log 2>&1 &
NEWPID=$!
disown
sleep 3
echo "MOCKS PID=$NEWPID"
ps -p $NEWPID -o pid=,etime=,cmd= || echo "(failed to start)"
echo "--- first lines ---"
head -n 25 /tmp/generate-mocks-live.log
