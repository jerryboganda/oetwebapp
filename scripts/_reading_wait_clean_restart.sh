#!/usr/bin/env bash
# Wait for reading orch to die, then clean manifest and restart with --resume.
set -uo pipefail

echo "[$(date)] waiting for reading orch to die..."
while pgrep -f 'generate-reading\.mjs' >/dev/null 2>&1; do
  sleep 30
done
echo "[$(date)] reading orch dead — cleaning manifest"

MANIFEST=/opt/oetwebapp/output/admin-bulk/generate-reading-resume.json
cp "$MANIFEST" "$MANIFEST.bak.$(date +%s)"

docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off -c "SELECT \"Title\" FROM \"ContentPapers\" WHERE \"SubtestCode\"='reading' AND \"Status\"=4;" 2>/dev/null > /tmp/db_titles.txt
echo "[$(date)] DB Status=4 reading count: $(wc -l < /tmp/db_titles.txt)"

node -e '
const fs = require("fs");
const manifest = JSON.parse(fs.readFileSync(process.argv[1],"utf8"));
const dbTitles = new Set(fs.readFileSync(process.argv[2],"utf8").split("\n").filter(Boolean));
const before = manifest.createdTitles.length;
manifest.createdTitles = manifest.createdTitles.filter(t => dbTitles.has(t));
fs.writeFileSync(process.argv[1], JSON.stringify(manifest, null, 2));
console.log("Manifest titles: " + before + " -> " + manifest.createdTitles.length + " (removed " + (before - manifest.createdTitles.length) + " phantoms)");
' "$MANIFEST" /tmp/db_titles.txt

echo "[$(date)] restarting reading orch"
cd /opt/oetwebapp
nohup node scripts/admin/generate-reading.mjs --count 110 --resume > /tmp/generate-reading-live.log 2>&1 &
NEW_PID=$!
echo "[$(date)] new reading PID=$NEW_PID"
sleep 5
ps -p $NEW_PID -o pid,etime,cmd 2>/dev/null
