#!/bin/bash
set -e
cd /opt/oetwebapp/backend/src/OetLearner.Api

# Verify the patched lines actually contain the new code (sanity check)
grep -c "DifficultyRating = meta?.DifficultyRating" Services/Listening/ListeningBackfillService.cs
grep -c "DifficultyLevel = q.DifficultyLevel" Services/Listening/ListeningBackfillService.cs

echo "=== Rebuild dll in API container ==="
docker exec -w /src/OetLearner.Api oet-api-green dotnet build OetLearner.Api.csproj --nologo -v q /p:UseAppHost=false 2>&1 | tail -10

echo "=== Restart API ==="
docker compose -f docker-compose.production.yml -f docker-compose.production.hostports.yml restart oet-api-green
sleep 8
docker exec oet-api-green curl -sS -o /dev/null -w 'health %{http_code}\n' http://127.0.0.1:8080/health
