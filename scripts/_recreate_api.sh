#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp

echo "--- BEFORE: AI key inside container (verbatim) ---"
docker exec oet-api-green env | grep '^AI__ApiKey=' || echo "no_line"

echo ""
echo "--- recreate oet-api-green with --env-file ---"
docker compose \
  --env-file .env.production \
  -p oetwebsite \
  -f docker-compose.production.yml \
  -f docker-compose.production.prebuilt-web.yml \
  up -d --no-deps --force-recreate oet-api-green 2>&1 | tail -5

sleep 12

echo ""
echo "--- AFTER: container info ---"
docker ps --filter name=oet-api-green --format 'STATUS={{.Status}}  CREATED={{.CreatedAt}}'

echo ""
echo "--- AFTER: AI key inside container ---"
docker exec oet-api-green env | grep '^AI__ApiKey=' | awk -F= '{ printf "%s=<len=%d>\n", $1, length($2) }'

echo ""
echo "--- API responsive? (auth-required endpoint should return 401 not 5xx) ---"
curl -s -o /dev/null -w "HTTP %{http_code}\n" https://api.oetwithdrhesham.co.uk/v1/admin/papers?limit=1
