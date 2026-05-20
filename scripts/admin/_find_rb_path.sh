#!/usr/bin/env bash
echo "=== find rulebook.v1.json ==="
docker exec oet-api-green find / -name 'rulebook.v1.json' 2>/dev/null
echo "=== find any *.json in /rulebooks ==="
docker exec oet-api-green sh -c 'ls -la /rulebooks 2>/dev/null; ls -la /app/rulebooks 2>/dev/null; ls -la /app/wwwroot/rulebooks 2>/dev/null'
echo "=== appsettings rulebook config ==="
docker exec oet-api-green cat /app/appsettings.Production.json | head -100
echo "=== env ==="
docker exec oet-api-green env | grep -iE 'rulebook|content|RULEBOOK' || true
echo "=== mounts ==="
docker inspect oet-api-green --format '{{range .Mounts}}{{.Type}} {{.Source}} -> {{.Destination}}{{println}}{{end}}'
