#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp

echo "=== PRE: current oet-* containers ==="
timeout 30 docker ps -a --format '{{.Names}} | {{.Image}} | {{.Status}}' | grep -i '^oet-' || echo none

echo "=== RETRY up -d --build (transient MCR pull reset previously) ==="
timeout 1200 docker compose -f docker-compose.vps.yml --env-file .env.production up -d --build --remove-orphans
echo "up exit=$?"

echo "=== POST: container inventory ==="
timeout 30 docker ps -a --format '{{.Names}} | {{.Image}} | {{.Status}}' | grep -i '^oet-' || echo none
echo "=== DONE ==="
