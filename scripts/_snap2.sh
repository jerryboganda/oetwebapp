#!/usr/bin/env bash
set -uo pipefail
echo "=== DRAFT/ARCHIVED LISTENING ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -At -F'|' -P pager=off -c \
  "SELECT \"Id\", \"Title\", \"Status\", \"UpdatedAt\" FROM \"ContentPapers\" WHERE \"SubtestCode\"='listening' AND \"Status\" IN (0,6) ORDER BY \"Status\", \"Title\";"
echo ""
echo "=== ALL COUNTS ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -At -F'|' -P pager=off -c \
  "SELECT \"SubtestCode\", \"Status\", COUNT(*) FROM \"ContentPapers\" GROUP BY \"SubtestCode\", \"Status\" ORDER BY \"SubtestCode\", \"Status\";"
echo ""
echo "=== PROCESSES ==="
ps -ef | grep -E 'generate-|retry-listening|_finalize' | grep -v grep || echo "NONE"
echo ""
echo "=== TAILS ==="
echo "--reading--"
tail -5 /tmp/generate-reading-live.log 2>/dev/null
echo "--retry-tts--"
tail -10 /tmp/retry-listening-tts.log 2>/dev/null
echo "--finalize--"
tail -8 /opt/oetwebapp/output/_finalize.log 2>/dev/null
