#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$repo_root"

if [ ! -f ".env.production" ]; then
  echo "[deploy-production] missing .env.production in $repo_root"
  exit 1
fi

echo "[deploy-production] stopping old containers..."
docker compose --env-file .env.production -f docker-compose.production.yml down --remove-orphans || true

echo "[deploy-production] building images (--no-cache --pull)..."
docker compose --env-file .env.production -f docker-compose.production.yml build --no-cache --pull

echo "[deploy-production] starting containers..."
docker compose --env-file .env.production -f docker-compose.production.yml up -d --force-recreate --remove-orphans

echo "[deploy-production] cleaning up old images..."
docker builder prune -af || true
docker image prune -af || true

echo "[deploy-production] container status:"
docker compose --env-file .env.production -f docker-compose.production.yml ps

echo "[deploy-production] deploy complete"