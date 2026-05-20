#!/bin/bash
docker exec -e PGPASSWORD="$PGPASS" oet-postgres psql -U oet_learner -d oet_learner -c "SELECT \"Id\",\"State\",\"Extension\",\"DeclaredMimeType\",\"OriginalFilename\",\"CreatedAt\" FROM \"AdminUploadSessions\" WHERE \"CreatedAt\" > NOW() - interval '6 hour' ORDER BY \"CreatedAt\" DESC LIMIT 30;"
echo
echo === manifest of reading orchestrator ===
cat /opt/oetwebapp/output/admin-bulk/generate-reading-manifest.json 2>/dev/null | head -50
echo
echo === manifest of listening orchestrator ===
cat /opt/oetwebapp/output/admin-bulk/generate-listening-manifest.json 2>/dev/null | head -30
echo
echo === recent failures log ===
ls -la /opt/oetwebapp/output/admin-bulk/failures-*.jsonl 2>/dev/null | tail -3
for f in $(ls -t /opt/oetwebapp/output/admin-bulk/failures-*.jsonl 2>/dev/null | head -3); do
  echo "--- $f ---"
  tail -10 "$f"
done
