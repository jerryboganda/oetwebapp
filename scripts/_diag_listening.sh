#!/usr/bin/env bash
PG_USER=$(docker exec oet-postgres bash -lc 'echo $POSTGRES_USER')
PG_DB=$(docker exec oet-postgres bash -lc 'echo $POSTGRES_DB')
echo "PG_USER=$PG_USER PG_DB=$PG_DB"
echo "=== DB COUNTS ==="
docker exec oet-postgres psql -U "$PG_USER" -d "$PG_DB" -tAc '
SELECT "SubtestCode","Status",count(*) FROM "ContentPapers" GROUP BY 1,2 ORDER BY 1,2;
'
echo
echo "=== DRAFT LISTENING ASSET MAP ==="
docker exec oet-postgres psql -U "$PG_USER" -d "$PG_DB" -tAc '
SELECT cp."Id",
       sum(case when cpa."Role"=0 and cpa."MediaAssetId" is not null then 1 else 0 end) as audio_ok,
       sum(case when cpa."Role"=0 then 1 else 0 end) as audio_rows_total
FROM "ContentPapers" cp
LEFT JOIN "ContentPaperAssets" cpa ON cpa."PaperId"=cp."Id"
WHERE cp."SubtestCode"='"'"'listening'"'"' AND cp."Status"=0
GROUP BY cp."Id"
ORDER BY audio_ok DESC, cp."Id" LIMIT 30;
'
echo
echo "=== RETRY-TTS LOG (last 60) ==="
tail -60 /tmp/retry-listening-tts.log 2>/dev/null
echo
echo "=== PROCESS ==="
ps -ef | grep -E 'retry-listening|generate-reading|generate-listening|finalize_when' | grep -v grep
