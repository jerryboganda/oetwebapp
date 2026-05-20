#!/usr/bin/env bash
set -uo pipefail
echo "=== sample storage paths from MediaAssets (audio-script-ish text files) ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -P pager=off -c 'SELECT ma."Id", ma."MimeType", ma."SizeBytes", ma."StoragePath" FROM "MediaAssets" ma JOIN "ContentPaperAssets" pa ON pa."MediaAssetId"=ma."Id" JOIN "ContentPapers" cp ON cp."Id"=pa."PaperId" WHERE cp."SubtestCode"=$$listening$$ AND cp."Status"=0 AND pa."Role"=2 LIMIT 3;' 2>&1
echo ""
echo "=== verify file exists in api container ==="
SP=$(docker exec oet-postgres psql -U oet_learner -d oet_learner -tA -c 'SELECT ma."StoragePath" FROM "MediaAssets" ma JOIN "ContentPaperAssets" pa ON pa."MediaAssetId"=ma."Id" JOIN "ContentPapers" cp ON cp."Id"=pa."PaperId" WHERE cp."SubtestCode"='"'"'listening'"'"' AND cp."Status"=0 AND pa."Role"=2 LIMIT 1;')
echo "SP=$SP"
docker exec oet-api-green ls -la "/var/opt/oet-learner/storage/$SP" 2>&1 | head -3
echo ""
echo "=== first 800 bytes of that file ==="
docker exec oet-api-green head -c 800 "/var/opt/oet-learner/storage/$SP" 2>&1
