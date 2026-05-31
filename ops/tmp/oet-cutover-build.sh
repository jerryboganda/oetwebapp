#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp || { echo "NO_APP_DIR"; exit 1; }

echo "=== PRE inventory (oet-*) ==="
timeout 30 docker ps -a --format '{{.Names}}\t{{.Image}}\t{{.Status}}' | grep -E '^oet-' || echo "(inventory hang or none)"

echo "=== build + up ==="
timeout 1800 docker compose -f docker-compose.vps.yml --env-file .env.production up -d --build --remove-orphans 2>&1 | tail -80
echo "UP_EXIT=${PIPESTATUS[0]}"

echo "=== POST inventory (oet-*) ==="
timeout 30 docker ps -a --format '{{.Names}}\t{{.Image}}\t{{.Status}}' | grep -E '^oet-' || echo "(inventory hang or none)"
echo "=== DONE CUTOVER ==="
