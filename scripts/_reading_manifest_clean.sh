#!/usr/bin/env bash
set -euo pipefail
# Reconcile reading resume manifest against DB.
# Removes phantom titles (in manifest but not Status=4 in DB) so orch can re-attempt those slots.

MANIFEST=/opt/oetwebapp/output/admin-bulk/generate-reading-resume.json
[ -f "$MANIFEST" ] || MANIFEST=/opt/oetwebapp/output/admin-bulk/generate-reading-manifest.json
echo "Using manifest: $MANIFEST"
cp "$MANIFEST" "$MANIFEST.bak.$(date +%s)"

# Get all Published reading paper titles from DB
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off -c "SELECT \"Title\" FROM \"ContentPapers\" WHERE \"SubtestCode\"='reading' AND \"Status\"=4;" 2>/dev/null > /tmp/db_titles.txt
echo "DB Status=4 reading count: $(wc -l < /tmp/db_titles.txt)"

# Read manifest titles
node -e '
const fs = require("fs");
const path = process.argv[1];
const dbPath = process.argv[2];
const manifest = JSON.parse(fs.readFileSync(path,"utf8"));
const dbTitles = new Set(fs.readFileSync(dbPath,"utf8").split("\n").filter(Boolean));
const before = manifest.createdTitles.length;
manifest.createdTitles = manifest.createdTitles.filter(t => dbTitles.has(t));
const after = manifest.createdTitles.length;
fs.writeFileSync(path, JSON.stringify(manifest, null, 2));
console.log(`Manifest titles: ${before} → ${after} (removed ${before-after} phantoms)`);
' "$MANIFEST" /tmp/db_titles.txt
