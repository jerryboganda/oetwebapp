#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp

echo "--- recreate learner-api-green ---"
docker compose \
  --env-file .env.production \
  -p oetwebsite \
  -f docker-compose.production.yml \
  -f docker-compose.production.prebuilt-web.yml \
  up -d --no-deps --force-recreate learner-api-green 2>&1 | tail -8

# Wait for new container to come up and pass health check
echo ""
for i in 1 2 3 4 5 6 7 8 9 10 11 12; do
  sleep 5
  STATUS=$(docker inspect -f '{{.State.Health.Status}}' oet-api-green 2>/dev/null || echo "absent")
  CREATED=$(docker inspect -f '{{.Created}}' oet-api-green 2>/dev/null || echo "absent")
  echo "[$((i*5))s] status=$STATUS  created=$CREATED"
  if [ "$STATUS" = "healthy" ]; then break; fi
done

echo ""
echo "--- AI key inside new container ---"
docker exec oet-api-green env | grep '^AI__ApiKey=' | awk -F= '{ printf "%s=<len=%d>\n", $1, length($2) }'

echo ""
echo "--- API responsive? ---"
curl -s -o /dev/null -w "HTTP %{http_code}\n" https://api.oetwithdrhesham.co.uk/v1/admin/papers?limit=1
