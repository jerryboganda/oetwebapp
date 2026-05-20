#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp

set +u
source scripts/admin/.envrc
set -u
KEY="${AI__ApiKey:-}"
echo "envrc key len=${#KEY}"
if [ -z "$KEY" ]; then echo "FATAL no key" >&2; exit 1; fi

STAMP=$(date +%Y%m%d-%H%M%S)
cp .env.production ".env.production.bak-aikey2-$STAMP"

# Remove any old mixed-case line, then add canonical AI__APIKEY=
grep -v '^AI__ApiKey=' .env.production | grep -v '^AI__APIKEY=' > .env.production.new
{ cat .env.production.new; echo "AI__APIKEY=$KEY"; } > .env.production
rm .env.production.new
chmod 600 .env.production

echo "--- .env.production AI lines ---"
grep -E '^AI__' .env.production | sed -E 's/=(.{6}).*/=\1.../'

echo ""
echo "--- recreate learner-api-green ---"
docker compose \
  --env-file .env.production \
  -p oetwebsite \
  -f docker-compose.production.yml \
  -f docker-compose.production.prebuilt-web.yml \
  up -d --no-deps --force-recreate learner-api-green 2>&1 | tail -5

for i in 1 2 3 4 5 6 7 8 9 10 11 12 13 14; do
  sleep 5
  STATUS=$(docker inspect -f '{{.State.Health.Status}}' oet-api-green 2>/dev/null || echo absent)
  echo "[$((i*5))s] $STATUS"
  if [ "$STATUS" = "healthy" ]; then break; fi
done

echo ""
echo "--- AI key inside new container ---"
docker exec oet-api-green env | grep '^AI__ApiKey=' | awk -F= '{ printf "%s=<len=%d>\n", $1, length($2) }'

echo ""
echo "--- API health ---"
curl -s -o /dev/null -w "HTTP %{http_code}\n" https://api.oetwithdrhesham.co.uk/v1/admin/papers?limit=1
