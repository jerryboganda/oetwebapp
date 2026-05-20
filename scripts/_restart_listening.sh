#!/usr/bin/env bash
set -u
cd /opt/oetwebapp

PGPASS='r50ATJJtDNBSH1AIBUfwa4Oeq5n8TjitxYzTAxmLOso0SKY0'

echo === killing listening ===
LPID=$(pgrep -fa 'node scripts/admin/generate-listening.mjs' | head -1 | awk '{print $1}')
if [ -n "$LPID" ]; then kill "$LPID" 2>/dev/null ; sleep 1 ; kill -9 "$LPID" 2>/dev/null ; echo "  killed $LPID" ; fi

echo === deleting recent draft listening ===
docker exec -e PGPASSWORD="$PGPASS" oet-postgres psql -U oet_learner -d oet_learner -c \
  "DELETE FROM \"ContentPapers\" WHERE \"Status\"=0 AND \"SubtestCode\"='listening' AND \"CreatedAt\" > NOW() - INTERVAL '6 hours';"

echo === reset manifest ===
printf '{"papers":[]}\n' > /opt/oetwebapp/output/admin-bulk/generate-listening-manifest.json

echo === launching listening ===
nohup bash scripts/admin/run-bulk.sh generate-listening.mjs > /tmp/generate-listening-live.log 2>&1 </dev/null & disown
sleep 2
LPID=$(pgrep -fa 'node scripts/admin/generate-listening.mjs' | head -1 | awk '{print $1}')
echo "  listening pid=$LPID"
ps -p "$LPID" -o pid,etime,cmd 2>&1 || echo "DEAD"
