#!/usr/bin/env bash
set -euo pipefail

echo "=== Delete 10 listening shells + children ==="
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -v ON_ERROR_STOP=1 <<'SQL'
\set ON_ERROR_STOP on
delete from "ContentPaperAssets" where "PaperId" in
  (select "Id" from "ContentPapers" where "SubtestCode"='listening' and "CreatedAt" > now() - interval '48 hours');
delete from "ListeningExtractionDrafts" where "PaperId" in
  (select "Id" from "ContentPapers" where "SubtestCode"='listening' and "CreatedAt" > now() - interval '48 hours');
delete from "MockBundleSections" where "ContentPaperId" in
  (select "Id" from "ContentPapers" where "SubtestCode"='listening' and "CreatedAt" > now() - interval '48 hours');
delete from "ContentPapers" where "SubtestCode"='listening' and "CreatedAt" > now() - interval '48 hours';
select 'remaining' as label, count(*) from "ContentPapers" where "SubtestCode"='listening';
SQL

echo "=== Reset manifest to correct shape ==="
MAN=/opt/oetwebapp/output/admin-bulk/generate-listening-manifest.json
echo '{"papers":[]}' > "$MAN"
cat "$MAN"; echo

echo "=== Kill stale + relaunch listening (no --resume; full 10 fresh) ==="
pkill -f generate-listening.mjs || true
sleep 2
cd /opt/oetwebapp
nohup bash scripts/admin/run-bulk.sh generate-listening.mjs > /tmp/generate-listening-live.log 2>&1 </dev/null &
disown
sleep 10
echo "=== Running ==="
ps -eo pid,etime,cmd | grep -E 'node scripts/admin/' | grep -v grep
echo "=== Tail ==="
tail -30 /tmp/generate-listening-live.log
