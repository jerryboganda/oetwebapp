#!/usr/bin/env bash
set +e
cd /opt/oetwebapp || { echo "cd_failed"; exit 1; }

echo '=== STEP 1: free router + green container names (NON-destructive, no volumes touched) ==='
timeout 60 docker rm -f oet-web oet-api oet-web-green oet-api-green 2>&1
echo "rm exit=$?"

echo '=== STEP 2: bring up single-container vps.yml stack ==='
timeout 600 docker compose -f docker-compose.vps.yml --env-file .env.production up -d --remove-orphans 2>&1
echo "up exit=$?"

echo '=== STEP 3: container inventory ==='
timeout 30 docker ps -a --filter label=com.docker.compose.project=oetwebsite --format '{{.Names}} | {{.Image}} | {{.Status}}' 2>&1

echo '=== DONE ==='
