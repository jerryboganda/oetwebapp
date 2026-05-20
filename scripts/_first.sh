#!/usr/bin/env bash
docker logs oet-api-green 2>&1 | grep -B1 -A12 "3728b30cad9240baa1e10d3e449e28bc\|2cd3f020e12741d89db96ef0bb4e98bf" | head -80
echo ===
echo "--- session row state ---"
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -c "SELECT \"Id\",\"State\",\"TotalParts\",\"PartsReceived\",\"ReceivedBytes\",\"DeclaredSizeBytes\",\"OriginalFilename\",\"DeclaredMimeType\",\"Extension\" FROM \"AdminUploadSessions\" WHERE \"Id\" IN ('09fc3ce3ed1f4a2ea3631ebdf4f23eeb','daccbf1584c5498b8b218d3f5d02ee92');"
