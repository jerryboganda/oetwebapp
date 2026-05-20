#!/usr/bin/env bash
sleep 480
echo '===== DAEMONS ====='
pgrep -af 'generate-reading|generate-listening|retry-listening-tts|_finalize_when_done' | head -10
echo ''
echo '===== READING tail ====='
tail -15 /tmp/generate-reading-live.log
echo ''
echo '===== RETRY tail ====='
tail -8 /tmp/retry-listening-tts.log
echo ''
echo '===== COUNTS ====='
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "SELECT \"SubtestCode\", \"Status\", COUNT(*) FROM \"ContentPapers\" WHERE \"SubtestCode\" IN ('reading','listening') GROUP BY 1,2 ORDER BY 1,2;"
