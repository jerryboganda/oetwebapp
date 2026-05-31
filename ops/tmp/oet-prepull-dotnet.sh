#!/usr/bin/env bash
set -u
cd /opt/oetwebapp || exit 2

pull_with_retry() {
  local img="$1"
  local n=0
  while [ "$n" -lt 6 ]; do
    n=$((n+1))
    echo "=== pull attempt $n: $img ==="
    if timeout 300 docker pull "$img"; then
      echo "OK: $img"
      return 0
    fi
    echo "retry $img in 5s..."
    sleep 5
  done
  echo "FAILED after retries: $img"
  return 1
}

pull_with_retry mcr.microsoft.com/dotnet/sdk:10.0
SDK=$?
pull_with_retry mcr.microsoft.com/dotnet/aspnet:10.0
ASP=$?

echo "=== local dotnet base images ==="
timeout 30 docker images | grep -E 'dotnet/(sdk|aspnet)' || echo "(none listed)"
echo "sdk_exit=$SDK aspnet_exit=$ASP"
echo "=== DONE PREPULL ==="
