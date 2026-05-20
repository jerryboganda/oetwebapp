#!/usr/bin/env bash
# Snapshot listening orch state.
set -uo pipefail

echo "=== LISTENING ORCH ==="
pgrep -af 'generate-listening\.mjs' || echo NO_PROC
echo
echo "=== LAST 15 LOG LINES ==="
tail -15 /tmp/generate-listening-live.log 2>/dev/null || echo NO_LOG
echo
echo "=== DB COUNTS BY STATUS ==="
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off -c "SELECT \"Status\", COUNT(*) FROM \"ContentPapers\" WHERE \"SubtestCode\"='listening' GROUP BY \"Status\" ORDER BY \"Status\";" 2>&1
echo
echo "=== LISTENING DRAFT TITLES ==="
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off -c "SELECT \"Title\" FROM \"ContentPapers\" WHERE \"SubtestCode\"='listening' AND \"Status\"=0 ORDER BY \"CreatedAt\";" 2>&1
echo
echo "=== LISTENING RESUME MANIFEST SIZE ==="
ls -la /opt/oetwebapp/output/admin-bulk/generate-listening-resume.json 2>/dev/null
if [ -f /opt/oetwebapp/output/admin-bulk/generate-listening-resume.json ]; then
  node -e 'const m=JSON.parse(require("fs").readFileSync("/opt/oetwebapp/output/admin-bulk/generate-listening-resume.json","utf8")); console.log("createdTitles:", m.createdTitles?.length || 0);'
fi
