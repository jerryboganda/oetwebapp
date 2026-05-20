#!/usr/bin/env bash
# Pull real ASP.NET exception stacks for grammar ai-draft 500s.
set -euo pipefail
echo "=== oet-api container stderr (real exceptions) ==="
docker logs --since 10m --tail 200 oet-api 2>&1 \
  | grep -vE '" (200|201|204|302|400|401|403|404|409|429) ' \
  | grep -iE 'exception|stack|fail|warn|aigateway|grammar|pronunciation|System\.|inner' \
  | tail -60 || echo "(none found in stderr)"
echo ""
echo "=== alt: any non-access lines from last 5m ==="
docker logs --since 5m --tail 500 oet-api 2>&1 | grep -v ' "POST \| "GET \| "PUT \| "DELETE ' | tail -40
