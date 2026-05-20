#!/usr/bin/env bash
set -euo pipefail
echo "=== /var/log mounts in oet-api ==="
docker exec oet-api sh -c 'ls -la /app/logs 2>/dev/null; ls -la /var/log/aspnet 2>/dev/null; ls -la /tmp 2>/dev/null | head -10' || echo "(no shell)"
echo ""
echo "=== oet-api volumes ==="
docker inspect oet-api --format '{{range .Mounts}}{{.Type}} {{.Source}} -> {{.Destination}}{{println}}{{end}}'
echo ""
echo "=== try internal log paths ==="
for p in /app/Logs /app/logs /var/log /tmp/oet-api.log /app/wwwroot/logs; do
  docker exec oet-api sh -c "[ -d '$p' ] && ls '$p' || [ -f '$p' ] && tail -30 '$p'" 2>/dev/null && echo "(found at $p)"
done
echo ""
echo "=== TAIL last 80 lines of full container logs (everything) ==="
docker logs --tail 80 oet-api 2>&1 | tail -80
