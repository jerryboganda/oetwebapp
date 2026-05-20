#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp

# 1. Source envrc to load AI__ApiKey into shell env
set +u
source scripts/admin/.envrc
set -u
if [ -z "${AI__ApiKey:-}" ]; then
  echo "FATAL: AI__ApiKey not in envrc" >&2
  exit 1
fi
echo "envrc-loaded AI__ApiKey len=${#AI__ApiKey}"

# 2. Backup and append to .env.production (idempotent)
STAMP=$(date +%Y%m%d-%H%M%S)
cp .env.production .env.production.bak-aikey-$STAMP
if grep -q '^AI__ApiKey=' .env.production; then
  echo "AI__ApiKey already present in .env.production; updating in place"
  sed -i "s|^AI__ApiKey=.*|AI__ApiKey=${AI__ApiKey}|" .env.production
else
  echo "" >> .env.production
  echo "# Added $(date) for reading orch" >> .env.production
  echo "AI__ApiKey=${AI__ApiKey}" >> .env.production
fi
grep '^AI__ApiKey=' .env.production | sed 's/=.*/=<set>/'

# 3. Restart only oet-api-green (the active slot) via docker compose
echo ""
echo "--- restarting oet-api-green ---"
docker compose -p oetwebsite -f docker-compose.production.yml -f docker-compose.production.prebuilt-web.yml up -d --no-deps --force-recreate oet-api-green 2>&1 | tail -10

# 4. Wait + verify
sleep 8
echo ""
echo "--- new container AI__ApiKey ---"
docker exec oet-api-green env | grep '^AI__ApiKey=' | sed 's/=.*/=<set>/' || echo missing
echo ""
echo "--- health check ---"
curl -fsSI https://api.oetwithdrhesham.co.uk/health 2>&1 | head -3 || echo health_fail
