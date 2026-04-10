#!/usr/bin/env bash

# ──────────────────────────────────────
# deploy-production.sh — bullet-proof version
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
echo "[deploy] disk space:"
df -h / | tail -1 || true
echo "[deploy] free memory:"
free -h 2>/dev/null || cat /proc/meminfo 2>/dev/null | head -3 || true

if [ ! -f ".env.production" ]; then
  echo "[deploy] FATAL: missing .env.production in $repo_root"
  exit 1
fi
echo "[deploy] .env.production exists ✓"

# ── Step 1: Stop old containers ──
echo ""
echo "[deploy] STEP 1/5: stopping old containers..."
if docker compose --env-file .env.production -f docker-compose.production.yml down --remove-orphans 2>&1; then
  echo "[deploy] STEP 1 ✓ containers stopped"
else
  echo "[deploy] STEP 1 ⚠ compose down returned $? (continuing anyway)"
fi

# ── Step 2: Build images ──
echo ""
echo "[deploy] STEP 2/5: building images (--no-cache --pull)..."
echo "[deploy] build started at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
if docker compose --env-file .env.production -f docker-compose.production.yml build --no-cache --pull 2>&1; then
  echo "[deploy] STEP 2 ✓ build succeeded at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
else
  BUILD_EXIT=$?
  echo "[deploy] STEP 2 ✗ BUILD FAILED with exit code $BUILD_EXIT at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "[deploy] aborting deploy — images were not built"
  exit 1
fi

# ── Step 3: Start containers ──
echo ""
echo "[deploy] STEP 3/5: starting containers..."
if docker compose --env-file .env.production -f docker-compose.production.yml up -d --force-recreate --remove-orphans 2>&1; then
  echo "[deploy] STEP 3 ✓ containers started"
else
  UP_EXIT=$?
  echo "[deploy] STEP 3 ✗ CONTAINER START FAILED with exit code $UP_EXIT"
  echo "[deploy] trying to show logs for debugging:"
  docker compose --env-file .env.production -f docker-compose.production.yml logs --tail=50 2>&1 || true
  exit 1
fi

# ── Step 4: Cleanup ──
echo ""
echo "[deploy] STEP 4/5: cleaning up old images..."
docker builder prune -af 2>&1 || echo "[deploy] STEP 4 ⚠ builder prune returned non-zero (ignored)"
docker image prune -af 2>&1 || echo "[deploy] STEP 4 ⚠ image prune returned non-zero (ignored)"
echo "[deploy] STEP 4 ✓ cleanup done"

# ── Step 5: Status ──
echo ""
echo "[deploy] STEP 5/5: container status:"
docker compose --env-file .env.production -f docker-compose.production.yml ps 2>&1 || true

echo ""
echo "============================================"
echo "[deploy] DEPLOY COMPLETE at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "[deploy] log saved to $LOGFILE"
echo "============================================"