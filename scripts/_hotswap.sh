#!/usr/bin/env bash
set -e
# Hot-swap DLL into oet-api-green
docker cp /opt/oetwebapp/backend/_build_publish/OetLearner.Api.dll oet-api-green:/app/OetLearner.Api.dll
docker cp /opt/oetwebapp/backend/_build_publish/OetLearner.Api.pdb oet-api-green:/app/OetLearner.Api.pdb 2>/dev/null || true
echo === restart ===
docker restart oet-api-green
sleep 10
echo === logs ===
docker logs --tail 15 oet-api-green 2>&1 | tail -15
echo === health ===
docker exec oet-api-green wget -qO- http://localhost:8080/health 2>&1 | head -3 || true
