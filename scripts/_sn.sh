#!/usr/bin/env bash
set -uo pipefail
echo "=== PROCESSES ==="
ps -ef | grep -E 'generate-(reading|listening)\.mjs|retry-listening|rwcr\.sh|_finalize' | grep -v grep || echo "NONE"
echo ""
echo "=== READING LOG TAIL ==="
tail -5 /tmp/generate-reading-live.log 2>/dev/null || echo "no log"
echo ""
echo "=== RETRY-TTS LOG TAIL ==="
tail -25 /tmp/retry-listening-tts.log 2>/dev/null || echo "no log"
echo ""
echo "=== FINALIZER LOG TAIL ==="
tail -3 /opt/oetwebapp/output/_finalize.log 2>/dev/null || echo "no log"
echo ""
echo "=== DB COUNTS ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off -c \
  "SELECT \"SubtestCode\", \"Status\", COUNT(*) FROM \"ContentPapers\" GROUP BY \"SubtestCode\", \"Status\" ORDER BY \"SubtestCode\", \"Status\";" 2>&1
echo ""
echo "=== DRAFT LISTENING ASSET MAP ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off -c \
  "SELECT cp.\"Id\", sum(case when cpa.\"Role\"=0 and cpa.\"MediaAssetId\" is not null then 1 else 0 end) FROM \"ContentPapers\" cp LEFT JOIN \"ContentPaperAssets\" cpa ON cpa.\"PaperId\"=cp.\"Id\" WHERE cp.\"SubtestCode\"='listening' AND cp.\"Status\"=0 GROUP BY cp.\"Id\" ORDER BY 2 DESC, cp.\"Id\";" 2>&1
