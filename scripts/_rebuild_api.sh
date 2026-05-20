#!/bin/bash
set -eo pipefail
cd /opt/oetwebapp
echo "=== Building OetLearner.Api via SDK container ==="
# global.json pins 10.0.201; the sdk:10.0 image has 10.0.300. Move it aside during build.
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
echo
echo "=== Verify text-file branch present in new dll ==="
strings /opt/oetwebapp/_build_out.dll | grep -E 'text/markdown|text/plain|Unrecognised' | head -5
echo
echo "=== Replacing dll in oet-api-green ==="
docker cp /opt/oetwebapp/_build_out.dll oet-api-green:/app/OetLearner.Api.dll
docker restart oet-api-green
sleep 8
echo
echo "=== Health check ==="
curl -sS -o /dev/null -w "api /health: %{http_code}\n" http://127.0.0.1:8080/health || true
docker logs --tail 25 oet-api-green 2>&1 | tail -25
