#!/usr/bin/env bash
# Production deploy — run on the VPS
# Matches AGENTS.md deploy runbook.
set -euo pipefail

cd /root/oetwebsite

echo "=== DEPLOY_START: $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="

echo "--- git fetch origin main ---"
git fetch origin main

echo "--- git reset --hard origin/main ---"
git reset --hard origin/main
echo "HEAD now at: $(git rev-parse HEAD)"
git log --oneline -1

echo "--- Building and starting containers (web + api; postgres stays up) ---"
docker compose --env-file .env.production -f docker-compose.production.yml up -d --build web learner-api

echo "--- Waiting 20s for healthchecks to start ---"
sleep 20

echo "--- Container status ---"
docker compose --env-file .env.production -f docker-compose.production.yml ps

echo "=== DEPLOY_DONE: $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
