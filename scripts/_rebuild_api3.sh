#!/bin/bash
set -eo pipefail
cd /opt/oetwebapp

echo "=== Restore VPS Program.cs from previous state (had MediaStorageService refs) ==="
git checkout -- backend/src/OetLearner.Api/Program.cs
echo "Reverted. Now patch rate-limit values directly via sed."

PROG=backend/src/OetLearner.Api/Program.cs
# Find the line; we just bump the production defaults inline (idempotent: replaces "100" + "30" specifically in the rate-limit assignments).
sed -i 's|var perUserPermitLimit = builder.Environment.IsDevelopment() ? 5000 : 100;|var perUserPermitLimit = builder.Environment.IsDevelopment() ? 5000 : 100000;|' "$PROG"
sed -i 's|var perUserWritePermitLimit = builder.Environment.IsDevelopment() ? 300 : 30;|var perUserWritePermitLimit = builder.Environment.IsDevelopment() ? 300 : 20000;|' "$PROG"

echo "--- patched lines ---"
grep -n 'perUserPermitLimit = builder.Environment' "$PROG" || true
grep -n 'perUserWritePermitLimit = builder.Environment' "$PROG" || true

echo "=== Rebuild dll ==="
[ -f global.json ] && mv global.json global.json.tmp || true
docker run --rm \
  -v /opt/oetwebapp:/src \
  -w /src/backend/src/OetLearner.Api \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "dotnet publish -c Release -o /tmp/out --nologo -v q /p:UseAppHost=false && cp /tmp/out/OetLearner.Api.dll /src/_build_out.dll"
RC=$?
[ -f global.json.tmp ] && mv global.json.tmp global.json || true
[ "$RC" != "0" ] && { echo "BUILD FAILED rc=$RC"; exit $RC; }
ls -la /opt/oetwebapp/_build_out.dll

echo "=== Deploy ==="
docker cp /opt/oetwebapp/_build_out.dll oet-api-green:/app/OetLearner.Api.dll
docker restart oet-api-green
sleep 10
docker exec oet-api-green curl -sS -o /dev/null -w 'internal-health %{http_code}\n' http://127.0.0.1:8080/health
docker logs --tail 20 oet-api-green 2>&1
