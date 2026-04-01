#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$repo_root"

if [ ! -f ".env.production" ]; then
  echo "[deploy-production] missing .env.production in $repo_root"
  exit 1
fi

docker compose --env-file .env.production -f docker-compose.production.yml down --remove-orphans || true
docker compose --env-file .env.production -f docker-compose.production.yml build --no-cache --pull
docker compose --env-file .env.production -f docker-compose.production.yml up -d --force-recreate --remove-orphans

docker builder prune -af
docker image prune -af

docker compose --env-file .env.production -f docker-compose.production.yml ps