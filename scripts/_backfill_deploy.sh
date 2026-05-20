#!/bin/bash
# Build + deploy backfill patch on VPS. Run via: ssh root@vps "bash /tmp/_backfill_deploy.sh"
set -eo pipefail
cd /opt/oetwebapp

PROG=backend/src/OetLearner.Api/Services/Listening/ListeningBackfillService.cs
echo "--- sanity check patched file ---"
grep -c "DifficultyRating = meta?.DifficultyRating" "$PROG"
grep -c "DifficultyLevel = q.DifficultyLevel" "$PROG"
grep -c "int? DifficultyLevel);" "$PROG"
grep -c "int? DifficultyRating," "$PROG"

# Re-apply rate-limit sed if not already (idempotent)
sed -i 's|var perUserPermitLimit = builder.Environment.IsDevelopment() ? 5000 : 100;|var perUserPermitLimit = builder.Environment.IsDevelopment() ? 5000 : 100000;|' backend/src/OetLearner.Api/Program.cs || true
sed -i 's|var perUserWritePermitLimit = builder.Environment.IsDevelopment() ? 300 : 30;|var perUserWritePermitLimit = builder.Environment.IsDevelopment() ? 300 : 20000;|' backend/src/OetLearner.Api/Program.cs || true

echo "=== Rebuild dll ==="
[ -f global.json ] && mv global.json global.json.tmp || true
set +e
docker run --rm \
  -v /opt/oetwebapp:/src \
  -w /src/backend/src/OetLearner.Api \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "dotnet publish -c Release -o /tmp/out --nologo -v q /p:UseAppHost=false && cp /tmp/out/OetLearner.Api.dll /src/_build_out.dll"
RC=$?
set -e
[ -f global.json.tmp ] && mv global.json.tmp global.json || true
if [ "$RC" != "0" ]; then echo "BUILD FAILED rc=$RC"; exit $RC; fi
ls -la /opt/oetwebapp/_build_out.dll

echo "=== Deploy ==="
docker cp /opt/oetwebapp/_build_out.dll oet-api-green:/app/OetLearner.Api.dll
docker restart oet-api-green
sleep 12
docker exec oet-api-green curl -sS -o /dev/null -w 'internal-health %{http_code}\n' http://127.0.0.1:8080/health
echo "=== last 15 log lines ==="
docker logs --tail 15 oet-api-green 2>&1
