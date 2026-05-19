#!/usr/bin/env bash
set +e
cd /opt/oetwebapp
echo "=== PIDs ==="
ps -p 2652714 -o pid,etime,cmd 2>/dev/null | tail -1
ps -p 3207996 -o pid,etime,cmd 2>/dev/null | tail -1
echo ""
echo "=== READING LOG (last 30 lines) ==="
tail -n 30 /tmp/generate-reading-live.log 2>/dev/null | sed 's/Bearer [A-Za-z0-9._-]\+/Bearer ***/g'
echo ""
echo "=== LISTENING LOG (last 30 lines) ==="
tail -n 30 /tmp/generate-listening-live.log 2>/dev/null | sed 's/Bearer [A-Za-z0-9._-]\+/Bearer ***/g'
echo ""
echo "=== DB STATUS ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "SELECT \"SubtestCode\", \"Status\", COUNT(*) FROM \"ContentPapers\" GROUP BY 1,2 ORDER BY 1,2;"
echo ""
echo "=== LISTENING DIFFICULTY-NULL CHECK ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -tA -c "SELECT COUNT(*) FROM \"ListeningExtracts\" WHERE \"DifficultyRating\" IS NULL;"
docker exec oet-postgres psql -U oet_learner -d oet_learner -tA -c "SELECT COUNT(*) FROM \"ListeningQuestions\" WHERE \"DifficultyLevel\" IS NULL;"
