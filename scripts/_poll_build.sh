#!/usr/bin/env bash
for i in 1 2 3 4 5 6 7 8 9 10 11 12; do
  sleep 30
  cnt=$(docker ps --filter ancestor=mcr.microsoft.com/dotnet/sdk:10.0 -q | wc -l)
  if [ "$cnt" -eq 0 ]; then
    echo "BUILD DONE at iter $i"
    break
  fi
  echo "iter $i: cnt=$cnt still running"
done
ls -la /opt/oetwebapp/backend/_build_publish/OetLearner.Api.dll
echo === recent docker events ===
docker events --since 20m --until 0s --filter "container=993b67a1ce9a" 2>&1 | tail -5 || true
