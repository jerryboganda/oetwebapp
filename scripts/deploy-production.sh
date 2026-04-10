#!/usr/bin/env bash

# ──────────────────────────────────────
# deploy-production.sh — bullet-proof version
# Aggressive cleanup BEFORE build to prevent disk-space failures.
# Logs every step; only exits 1 on critical failure.
# ──────────────────────────────────────

LOGFILE="/tmp/deploy-production-$(date +%s).log"
exec > >(tee "$LOGFILE") 2>&1

echo "============================================"
echo "[deploy] started at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "============================================"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
echo "[deploy] working directory: $(pwd)"

# ── Pre-flight checks ──
echo "[deploy] docker version:"
docker --version || true
echo "[deploy] docker compose version:"
docker compose version || true
echo "[deploy] disk space BEFORE cleanup:"
df -h / | tail -1 || true
echo "[deploy] free memory:"
free -h 2>/dev/null || cat /proc/meminfo 2>/dev/null | head -3 || true

if [ ! -f ".env.production" ]; then
  echo "[deploy] FATAL: missing .env.production in $repo_root"
  exit 1
fi
echo "[deploy] .env.production exists"

# ── Step 1: Stop old containers ──
echo ""
echo "[deploy] STEP 1/6: stopping old containers..."
docker compose --env-file .env.production -f docker-compose.production.yml down --remove-orphans 2>&1 || echo "[deploy] STEP 1 warn: compose down returned non-zero (continuing)"
echo "[deploy] STEP 1 done"

# ── Step 2: Aggressive pre-build cleanup (free disk space) ──
echo ""
echo "[deploy] STEP 2/6: aggressive pre-build cleanup..."
echo "[deploy] removing ALL unused containers..."
docker container prune -f 2>&1 || true
echo "[deploy] removing ALL unused images..."
docker image prune -af 2>&1 || true
echo "[deploy] removing ALL build cache..."
docker builder prune -af 2>&1 || true
echo "[deploy] removing ALL unused volumes (except named)..."
docker volume prune -f 2>&1 || true
echo "[deploy] removing ALL unused networks..."
docker network prune -f 2>&1 || true
echo "[deploy] disk space AFTER cleanup:"
df -h / | tail -1 || true
echo "[deploy] STEP 2 done"

# ── Step 3: Build images ──
echo ""
echo "[deploy] STEP 3/6: building images..."
echo "[deploy] build started at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
if docker compose --env-file .env.production -f docker-compose.production.yml build --pull 2>&1; then
  echo "[deploy] STEP 3 OK: build succeeded at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
else
  BUILD_EXIT=$?
  echo "[deploy] STEP 3 FAILED: build exit code $BUILD_EXIT at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "[deploy] disk space after failed build:"
  df -h / | tail -1 || true
  exit 1
fi

# ── Step 4: Start containers ──
echo ""
echo "[deploy] STEP 4/6: starting containers..."
if docker compose --env-file .env.production -f docker-compose.production.yml up -d --force-recreate --remove-orphans 2>&1; then
  echo "[deploy] STEP 4 OK: containers started"
else
  UP_EXIT=$?
  echo "[deploy] STEP 4 FAILED: container start exit code $UP_EXIT"
  docker compose --env-file .env.production -f docker-compose.production.yml logs --tail=50 2>&1 || true
  exit 1
fi

# ── Step 5: Post-build cleanup ──
echo ""
echo "[deploy] STEP 5/6: post-build cleanup..."
docker builder prune -af 2>&1 || true
docker image prune -af 2>&1 || true
echo "[deploy] STEP 5 done"

# ── Step 6: Status ──
echo ""
echo "[deploy] STEP 6/6: container status:"
docker compose --env-file .env.production -f docker-compose.production.yml ps 2>&1 || true

echo ""
echo "[deploy] disk space FINAL:"
df -h / | tail -1 || true
echo ""
echo "============================================"
echo "[deploy] DEPLOY COMPLETE at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "[deploy] log saved to $LOGFILE"
echo "============================================"