#!/usr/bin/env bash
set -u
cd /opt/oetwebapp

PGPASS='r50ATJJtDNBSH1AIBUfwa4Oeq5n8TjitxYzTAxmLOso0SKY0'

echo === killing orchestrators ===
for p in 2428881 2428954; do
  if ps -p "$p" > /dev/null 2>&1; then
    kill "$p" 2>/dev/null || true
    sleep 1
    kill -9 "$p" 2>/dev/null || true
    echo "  killed $p"
  fi
done

echo === deleting recent draft listening + reading papers ===
docker exec -e PGPASSWORD="$PGPASS" oet-postgres psql -U oet_learner -d oet_learner -c \
  "DELETE FROM \"ContentPapers\" WHERE \"Status\"=0 AND \"SubtestCode\" IN ('listening','reading') AND \"CreatedAt\" > NOW() - INTERVAL '6 hours';"

echo === resetting manifests ===
printf '{"papers":[]}\n' > /opt/oetwebapp/output/admin-bulk/generate-listening-manifest.json
printf '{"papers":[]}\n' > /opt/oetwebapp/output/admin-bulk/generate-reading-manifest.json

echo === launching listening ===
nohup bash scripts/admin/run-bulk.sh generate-listening.mjs > /tmp/generate-listening-live.log 2>&1 </dev/null & disown
sleep 2
LPID=$(pgrep -fa 'node scripts/admin/generate-listening.mjs' | head -1 | awk '{print $1}')
echo "  listening pid=$LPID"

echo === launching reading ===
nohup bash scripts/admin/run-bulk.sh generate-reading.mjs > /tmp/generate-reading-live.log 2>&1 </dev/null & disown
sleep 2
RPID=$(pgrep -fa 'node scripts/admin/generate-reading.mjs' | head -1 | awk '{print $1}')
echo "  reading pid=$RPID"

echo === alive check ===
ps -p "$LPID" -o pid,etime,cmd 2>&1 || echo "listening DEAD"
ps -p "$RPID" -o pid,etime,cmd 2>&1 || echo "reading DEAD"
