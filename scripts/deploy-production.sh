#!/usr/bin/env bash

# --------------------------------------
# deploy-production.sh - bullet-proof version
# Aggressive cleanup BEFORE build to prevent disk-space failures.
# Logs every step; only exits 1 on critical failure.
# --------------------------------------
set -Eeuo pipefail

LOGFILE="/tmp/deploy-production-$(date +%s).log"
exec > >(tee "$LOGFILE") 2>&1

echo "============================================"
echo "[deploy] started at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "============================================"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
echo "[deploy] working directory: $(pwd)"

# -- Pre-flight checks --
echo "[deploy] docker version:"
docker --version || true
echo "[deploy] docker compose version:"
docker compose version || true
export COMPOSE_PARALLEL_LIMIT="${COMPOSE_PARALLEL_LIMIT:-1}"
echo "[deploy] compose parallel limit: $COMPOSE_PARALLEL_LIMIT"
echo "[deploy] disk space BEFORE cleanup:"
df -h / | tail -1 || true
echo "[deploy] free memory:"
free -h 2>/dev/null || cat /proc/meminfo 2>/dev/null | head -3 || true

if [ ! -f ".env.production" ]; then
  echo "[deploy] FATAL: missing .env.production in $repo_root"
  exit 1
fi
echo "[deploy] .env.production exists"
bash scripts/deploy/validate-production-env.sh .env.production
bash scripts/deploy/mock-stub-scan.sh .env.production

# -- Step 1: Pre-build cleanup (free disk space without destroying layer cache) --
echo ""
echo "[deploy] STEP 1/5: pre-build cleanup..."
echo "[deploy] removing stopped containers..."
docker container prune -f 2>&1 || true
echo "[deploy] removing dangling images (preserving tagged/cached images)..."
docker image prune -f 2>&1 || true
echo "[deploy] removing unused build cache older than 24h..."
docker builder prune -f --filter "until=24h" 2>&1 || true
echo "[deploy] disk space AFTER cleanup:"
df -h / | tail -1 || true
echo "[deploy] STEP 1 done"

# -- Step 2: Build images before touching the live containers. If the build
# fails, the current production stack stays up instead of being replaced by 502s.
echo ""
echo "[deploy] STEP 2/5: building images..."
echo "[deploy] build started at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
build_images() {
  local service
  for service in db-backup learner-api web; do
    echo "[deploy] building $service"
    docker compose --env-file .env.production -f docker-compose.production.yml build --pull "$service" 2>&1 || return $?
  done
}

if build_images; then
  echo "[deploy] STEP 2 OK: build succeeded at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
else
  BUILD_EXIT=$?
  echo "[deploy] STEP 2 WARN: initial build failed with exit code $BUILD_EXIT at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "[deploy] pruning BuildKit cache and retrying once; this recovers stale/corrupt package-cache mounts without touching volumes..."
  docker builder prune -af 2>&1 || true
  if build_images; then
    echo "[deploy] STEP 2 OK: build succeeded after cache prune at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  else
    BUILD_RETRY_EXIT=$?
    echo "[deploy] STEP 2 FAILED: retry build exit code $BUILD_RETRY_EXIT at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "[deploy] disk space after failed build retry:"
    df -h / | tail -1 || true
    exit 1
  fi
fi

# -- Step 3: Start containers --
echo ""
echo "[deploy] STEP 3/5: starting containers..."
if docker compose --env-file .env.production -f docker-compose.production.yml up -d --force-recreate --remove-orphans 2>&1; then
  echo "[deploy] STEP 3 OK: containers started"
else
  UP_EXIT=$?
  echo "[deploy] STEP 3 FAILED: container start exit code $UP_EXIT"
  docker compose --env-file .env.production -f docker-compose.production.yml logs --tail=50 2>&1 || true
  exit 1
fi

# -- Step 4: Post-build cleanup --
echo ""
echo "[deploy] STEP 4/5: post-build cleanup..."
docker builder prune -af 2>&1 || true
docker image prune -af 2>&1 || true
echo "[deploy] STEP 4 done"

# -- Step 5: Status --
echo ""
echo "[deploy] STEP 5/5: container status:"
docker compose --env-file .env.production -f docker-compose.production.yml ps 2>&1 || true

echo ""
echo "[deploy] disk space FINAL:"
df -h / | tail -1 || true
echo ""
echo "============================================"
echo "[deploy] DEPLOY COMPLETE at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "[deploy] log saved to $LOGFILE"
echo "============================================"

