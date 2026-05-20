#!/bin/bash
set +e
echo "=== PROCS ==="
ps -ef | grep -E 'generate|publish-vocab' | grep -v grep
echo "=== TTS test Cherry ==="
curl -sS -X POST 'https://inference.do-ai.run/v1/audio/speech' \
  -H "authorization: Bearer ${DO_AI_TOKEN:?set DO_AI_TOKEN}" \
  -H 'content-type: application/json' \
  -d '{"model":"qwen3-tts-voicedesign","input":"Hello, this is a test for OET listening practice.","voice":"Cherry","instructions":"A calm clear professional native English voice."}' \
  -o /tmp/tts1.bin -w 'http=%{http_code} size=%{size_download}\n'
file /tmp/tts1.bin
head -c 300 /tmp/tts1.bin; echo
echo "=== Models tts/voice/speech/audio ==="
curl -sS 'https://inference.do-ai.run/v1/models' -H "authorization: Bearer ${DO_AI_TOKEN:?set DO_AI_TOKEN}" > /tmp/models.json
grep -oE '"id":"[^"]+"' /tmp/models.json | grep -iE 'tts|voice|speech|audio'
echo "=== DB counts ==="
USER=$(docker exec oet-postgres bash -c 'echo $POSTGRES_USER')
DB=$(docker exec oet-postgres bash -c 'echo $POSTGRES_DB')
docker exec oet-postgres psql -U "$USER" -d "$DB" -tA -c "SELECT \"SubtestCode\",\"Status\",COUNT(*) FROM \"ContentPapers\" GROUP BY 1,2 ORDER BY 1,2;"
echo "=== Speaking-assets log ==="
tail -25 /tmp/generate-speaking-assets-live.log 2>/dev/null || echo NONE
