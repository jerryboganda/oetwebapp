#!/usr/bin/env bash
echo "=== running PIDs ==="
for n in 'generate-listening.mjs' 'generate-reading.mjs' 'publish-vocab' 'generate-mocks.mjs' 'generate-grammar.mjs' 'generate-pronunciation.mjs'; do
  p=$(pgrep -f "$n" || echo NONE)
  printf '  %-35s -> %s\n' "$n" "$p"
done
echo
echo "=== listening tail ==="
tail -n 12 /tmp/generate-listening-live.log 2>/dev/null || echo none
echo
echo "=== reading tail ==="
tail -n 12 /tmp/generate-reading-live.log 2>/dev/null || echo none
echo
echo "=== vocab tail ==="
tail -n 5 /tmp/publish-vocab-live.log 2>/dev/null || tail -n 5 /tmp/vocab-live.log 2>/dev/null || echo none
echo
echo "=== api-green recent errors (last 4 min) ==="
docker logs --since 4m oet-api-green 2>&1 | grep -iE 'error|exception|denied|reject|fail' | grep -viE 'success|debug|trace' | tail -n 20
echo
echo "=== published counts ==="
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -t -c "SELECT \"SubtestCode\", \"Status\", COUNT(*) FROM \"ContentPapers\" GROUP BY \"SubtestCode\", \"Status\" ORDER BY \"SubtestCode\", \"Status\";"
