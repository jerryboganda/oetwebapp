#!/usr/bin/env bash
echo "=== /uploads activity (last 8 min) ==="
docker logs --since 8m oet-api-green 2>&1 | grep -E "POST /v1/admin/uploads|PUT /v1/admin/uploads|complete|abort" | tail -40
echo
echo "=== exceptions surrounding session daccbf158 / 09fc3ce ==="
docker logs --since 12m oet-api-green 2>&1 | grep -B1 -A6 -iE "daccbf15|09fc3ce3|staged parts|abort" | tail -60
