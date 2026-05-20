#!/usr/bin/env bash
# Snapshot orchestrator + DB state
set -uo pipefail
echo "=== PROCESSES ==="
ps -ef | grep -E 'generate-(reading|listening)\.mjs|rwcr\.sh|_finalize' | grep -v grep || echo "NONE"
echo ""
echo "=== READING LOG TAIL ==="
tail -5 /tmp/generate-reading-live.log 2>/dev/null || echo "no log"
echo ""
echo "=== LISTENING LOG TAIL ==="
tail -8 /tmp/generate-listening-live.log 2>/dev/null || echo "no log"
echo ""
echo "=== RWCR LOG ==="
cat /tmp/rwcr.log 2>/dev/null || echo "no log"
echo ""
echo "=== FINALIZER LOG TAIL ==="
tail -3 /opt/oetwebapp/output/_finalize.log 2>/dev/null || echo "no log"
echo ""
echo "=== DB COUNTS ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off -c \
  "SELECT \"SubtestCode\", \"Status\", COUNT(*) FROM \"ContentPapers\" GROUP BY \"SubtestCode\", \"Status\" ORDER BY \"SubtestCode\", \"Status\";" 2>&1
echo ""
echo "=== DRAFT LISTENING TITLES ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off -c \
  "SELECT \"Id\", \"Title\" FROM \"ContentPapers\" WHERE \"SubtestCode\"='listening' AND \"Status\"=0 ORDER BY \"CreatedAt\";" 2>&1
