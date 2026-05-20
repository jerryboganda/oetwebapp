#!/bin/bash
set -eo pipefail
cd /opt/oetwebapp

echo "=== Sync Program.cs ==="
cp /tmp/Program.cs backend/src/OetLearner.Api/Program.cs
grep -n 'PerUserWritePermitLimit' backend/src/OetLearner.Api/Program.cs | head -5

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

echo "=== Existing appsettings on container ==="
docker exec oet-api-green ls -la /app/ | grep -i appsetting || true

echo "=== Read existing appsettings.Production.json (if any) ==="
docker exec oet-api-green sh -c 'test -f /app/appsettings.Production.json && cat /app/appsettings.Production.json || echo NO_PROD_APPSETTINGS' > /tmp/_prodset.json
head -100 /tmp/_prodset.json
echo

echo "=== Merge RateLimit override via jq ==="
if grep -q NO_PROD_APPSETTINGS /tmp/_prodset.json; then
  cat > /tmp/_prodset.json <<'JSON'
{
  "RateLimit": {
    "PerUserPermitLimit": 100000,
    "PerUserWritePermitLimit": 20000
  }
}
JSON
else
  jq '.RateLimit = (.RateLimit // {}) + {"PerUserPermitLimit": 100000, "PerUserWritePermitLimit": 20000}' /tmp/_prodset.json > /tmp/_prodset.merged.json
  mv /tmp/_prodset.merged.json /tmp/_prodset.json
fi
echo "--- merged ---"
cat /tmp/_prodset.json

echo "=== Deploy dll + appsettings ==="
docker cp /opt/oetwebapp/_build_out.dll oet-api-green:/app/OetLearner.Api.dll
docker cp /tmp/_prodset.json oet-api-green:/app/appsettings.Production.json
docker restart oet-api-green
sleep 10
docker exec oet-api-green curl -sS -o /dev/null -w 'internal-health %{http_code}\n' http://127.0.0.1:8080/health
echo "--- last 25 log lines ---"
docker logs --tail 25 oet-api-green 2>&1
