#!/usr/bin/env bash
# Direct deploy — pull from Git, build locally, and roll out.
# Use for owner-initiated quick deploys when CI/evidence gates are not needed.
# Usage:
#   DEPLOY_REF=main bash scripts/deploy/deploy-direct.sh
#   DEPLOY_REF=<branch-or-sha> bash scripts/deploy/deploy-direct.sh
set -euo pipefail

APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
DEPLOY_REF="${DEPLOY_REF:-main}"
APP_PUBLIC_URL="${APP_PUBLIC_URL:-https://app.oetwithdrhesham.co.uk}"
API_PUBLIC_URL="${API_PUBLIC_URL:-https://api.oetwithdrhesham.co.uk}"

cd "$APP_DIR"

echo "=== DIRECT_DEPLOY_START: $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
echo "Target ref: $DEPLOY_REF"

# --- Fetch and checkout ---
echo "--- git fetch ---"
git fetch origin --prune
if git rev-parse --verify "$DEPLOY_REF^{commit}" >/dev/null 2>&1; then
  DEPLOY_SHA="$(git rev-parse --verify "$DEPLOY_REF^{commit}")"
else
  git fetch origin "$DEPLOY_REF"
  DEPLOY_SHA="$(git rev-parse --verify "origin/$DEPLOY_REF^{commit}" 2>/dev/null || git rev-parse --verify "$DEPLOY_REF^{commit}")"
fi

echo "Deploying SHA: $DEPLOY_SHA"
git reset --hard "$DEPLOY_SHA"
git log --oneline -1

# --- Validate env file exists ---
if [ ! -f .env.production ]; then
  echo ".env.production not found in $APP_DIR — aborting." >&2
  exit 1
fi

# --- Determine target slot (blue/green toggle) ---
previous_slot=""
if [ -s .deploy/previous-good.env ]; then
  previous_slot=$(awk -F= '$1 == "PREVIOUS_GOOD_SLOT" { print $2 }' .deploy/previous-good.env | tail -n 1)
fi
if [ -s .deploy/active-slot.env ]; then
  previous_slot=$(awk -F= '$1 == "ACTIVE_SLOT" { print $2 }' .deploy/active-slot.env | tail -n 1)
fi

case "$previous_slot" in
  blue) target_slot="green" ;;
  green) target_slot="blue" ;;
  "") target_slot="blue" ;;
  *) target_slot="blue" ;;
esac

echo "--- Deploying to slot: $target_slot (previous: ${previous_slot:-none}) ---"

compose() {
  ACTIVE_SLOT="$target_slot" docker compose \
    --env-file .env.production \
    -f docker-compose.production.yml \
    -f docker-compose.production.build.yml \
    "$@"
}

# --- Build images from source ---
# Build sequentially with --no-cache to avoid OOM (parallel builds exhaust
# swap) and to bypass any stale BuildKit content-store snapshot refs left
# by interrupted prior runs.
echo "--- Building images from source (sequential, no-cache) ---"
echo "--- Stopping inactive slot + non-essential containers to free RAM ---"
docker stop "oet-api-${target_slot}" "oet-web-${target_slot}" oet-clamav 2>/dev/null || true
echo "--- Building db-backup ---"
compose build --no-cache db-backup
echo "--- Building learner-api-${target_slot} ---"
compose build --no-cache "learner-api-$target_slot"
echo "--- Building web-${target_slot} ---"
compose build --no-cache "web-$target_slot"

# --- Start target slot ---
echo "--- Starting target slot containers ---"
compose up -d --no-build --force-recreate "learner-api-$target_slot" "web-$target_slot" db-backup

# --- Wait for health ---
echo "--- Waiting for target slot health ---"
healthcheck() {
  local container="$1"
  local check="$2"
  local label="$3"
  local max_attempts="${4:-60}"
  for i in $(seq 1 "$max_attempts"); do
    if docker exec "$container" sh -c "$check" >/dev/null 2>&1; then
      echo "  ✓ $label healthy (attempt $i)"
      return 0
    fi
    if [ "$i" -eq "$max_attempts" ]; then
      echo "  ✗ $label failed health check after $max_attempts attempts" >&2
      docker logs --tail=30 "$container" >&2 || true
      return 1
    fi
    sleep 5
  done
}

healthcheck "oet-api-$target_slot" "curl --fail --silent http://127.0.0.1:8080/health/ready" "API ($target_slot)"
healthcheck "oet-web-$target_slot" "wget -qO- http://127.0.0.1:3000/api/health" "Web ($target_slot)"

# --- Switch stable routers ---
echo "--- Switching routers to $target_slot ---"
ACTIVE_SLOT="$target_slot" docker compose \
  --env-file .env.production \
  -f docker-compose.production.yml \
  up -d --no-build --force-recreate learner-api web

sleep 5

# --- Verify public endpoints ---
echo "--- Verifying public endpoints ---"
api_ok=false
web_ok=false

for i in $(seq 1 12); do
  if curl --fail --silent --max-time 10 "$API_PUBLIC_URL/health/ready" >/dev/null 2>&1; then
    api_ok=true
    break
  fi
  sleep 5
done

for i in $(seq 1 12); do
  if curl --fail --silent --max-time 10 "$APP_PUBLIC_URL/api/health" >/dev/null 2>&1; then
    web_ok=true
    break
  fi
  sleep 5
done

if [ "$api_ok" = "true" ] && [ "$web_ok" = "true" ]; then
  echo "  ✓ API public health: OK"
  echo "  ✓ Web public health: OK"
else
  echo "  ⚠ Public health check issues:" >&2
  [ "$api_ok" != "true" ] && echo "    API ($API_PUBLIC_URL): FAILED" >&2
  [ "$web_ok" != "true" ] && echo "    Web ($APP_PUBLIC_URL): FAILED" >&2
  echo "  Rolling back to $previous_slot..." >&2
  if [ -n "$previous_slot" ]; then
    ACTIVE_SLOT="$previous_slot" docker compose \
      --env-file .env.production \
      -f docker-compose.production.yml \
      up -d --no-build --force-recreate learner-api web || true
  fi
  exit 1
fi

# --- Record successful deploy ---
mkdir -p .deploy
current_sha=$(git rev-parse HEAD)
{
  echo "PREVIOUS_GOOD_SHA=$current_sha"
  echo "PREVIOUS_GOOD_SLOT=$target_slot"
  echo "PREVIOUS_GOOD_RECORDED_AT_UTC=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  [ -n "$previous_slot" ] && echo "ROLLED_FROM_SLOT=$previous_slot"
} > .deploy/previous-good.env
echo "ACTIVE_SLOT=$target_slot" > .deploy/active-slot.env

# --- Stop previous slot to prevent chunk-hash mismatch ---
# Each Next.js build produces unique content-hashed chunk filenames in .next/static/chunks/.
# If both slots stay running, any request to the router that ends up at the wrong slot
# (e.g. due to NPM upstream config or DNS caching) returns 404 for chunks compiled in the
# other slot, which renders the page blank in the browser. Stop the inactive slot to
# guarantee all /_next/static/* requests resolve to the active build.
if [ -n "$previous_slot" ] && [ "$previous_slot" != "$target_slot" ]; then
  echo "--- Stopping previous slot ($previous_slot) to prevent static-chunk mismatch ---"
  docker stop "oet-web-$previous_slot" "oet-api-$previous_slot" 2>/dev/null || true
fi

echo ""
echo "=== DIRECT_DEPLOY_DONE: $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
echo "  SHA: $current_sha"
echo "  Slot: $target_slot"
echo "  API: $API_PUBLIC_URL"
echo "  Web: $APP_PUBLIC_URL"
