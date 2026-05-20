#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp
echo "=== active slot ==="
cat .deploy/active-slot.env
echo "=== oet containers ==="
docker ps --filter name=oet- --format '{{.Names}}\t{{.Status}}'
echo "=== git HEAD ==="
git log --oneline -1
echo "=== listening status counts ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -t -c "SELECT \"Status\", COUNT(*) FROM \"ContentPapers\" WHERE \"SubtestCode\"='listening' GROUP BY \"Status\" ORDER BY \"Status\";"
echo "=== drafts without audio asset ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -t -c "SELECT p.\"Id\", p.\"Title\", p.\"Status\", (SELECT COUNT(*) FROM \"ContentPaperAssets\" a WHERE a.\"PaperId\"=p.\"Id\" AND a.\"Role\"=0) AS audio_assets FROM \"ContentPapers\" p WHERE p.\"SubtestCode\"='listening' AND p.\"Status\"=0 ORDER BY p.\"CreatedAt\" LIMIT 30;"
echo "=== api active container env elevenlabs check ==="
ACTIVE=$(awk -F= '/ACTIVE_SLOT/{print $2}' .deploy/active-slot.env)
if [ "$ACTIVE" = "blue" ]; then C=oet-api; else C=oet-api-green; fi
docker exec "$C" env | grep -i elevenlabs | sed 's/=.*/=<set>/'
