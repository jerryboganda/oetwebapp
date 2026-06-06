#!/usr/bin/env bash
# Memory-safe fresh deploy from pre-built GHCR images (NO build on the VPS).
#
# The prod VPS is a shared host (60+ co-tenant containers); building Next.js
# in-place OOM-cascades the whole box. So images are built off-box in CI
# (.github/workflows/deploy.yml) and this script only PULLS + recreates
# containers. Blue/green with a health gate: a broken commit fails the gate on
# the inactive slot and the router is NOT flipped, so production stays up.
#
# Invoked by CI over SSH as:
#   WEB_IMAGE=ghcr.io/<owner>/oetwebapp-web:<sha> \
#   API_IMAGE=ghcr.io/<owner>/oetwebapp-api:<sha> \
#   bash scripts/deploy/auto-deploy-ghcr.sh
set -euo pipefail

APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
APP_PUBLIC_URL="${APP_PUBLIC_URL:-https://app.oetwithdrhesham.co.uk}"
API_PUBLIC_URL="${API_PUBLIC_URL:-https://api.oetwithdrhesham.co.uk}"
: "${WEB_IMAGE:?Set WEB_IMAGE to the GHCR web image ref}"
: "${API_IMAGE:?Set API_IMAGE to the GHCR api image ref}"
cd "$APP_DIR"

echo "=== AUTO_DEPLOY_START $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
echo "WEB_IMAGE=$WEB_IMAGE"
echo "API_IMAGE=$API_IMAGE"

echo "--- validating production env ---"
bash scripts/deploy/validate-production-env.sh .env.production

# --- pick the inactive (target) slot ---
prev_slot="green"
if [ -s .deploy/active-slot.env ]; then
  prev_slot="$(awk -F= '$1=="ACTIVE_SLOT"{print $2}' .deploy/active-slot.env | tail -n1)"
fi
case "$prev_slot" in
  blue)  target_slot="green" ;;
  green) target_slot="blue"  ;;
  *)     target_slot="blue"  ;;
esac
echo "active slot: ${prev_slot:-none} -> deploying to: $target_slot"

# --- persist image refs so any future manual compose op uses them too ---
mkdir -p .deploy
for kv in "WEB_IMAGE=$WEB_IMAGE" "API_IMAGE=$API_IMAGE"; do
  key="${kv%%=*}"
  if grep -q "^${key}=" .env.production 2>/dev/null; then
    sed -i "s#^${key}=.*#${kv}#" .env.production
  else
    echo "$kv" >> .env.production
  fi
done

export WEB_IMAGE API_IMAGE
compose() { ACTIVE_SLOT="$1" docker compose --env-file .env.production -f docker-compose.production.yml "${@:2}"; }

# --- pull the freshly-built images (no build here) ---
echo "--- pulling images ---"
docker pull "$WEB_IMAGE"
docker pull "$API_IMAGE"

# --- start the target slot from the new images ---
echo "--- starting target slot ($target_slot) ---"
compose "$target_slot" up -d --no-build --force-recreate \
  "web-$target_slot" "learner-api-$target_slot" db-backup

# --- health gate on the target slot (prod still served by $prev_slot) ---
healthcheck() {
  local container="$1" check="$2" label="$3" max="${4:-50}"
  for i in $(seq 1 "$max"); do
    if docker exec "$container" sh -c "$check" >/dev/null 2>&1; then
      echo "  OK: $label (attempt $i)"; return 0
    fi
    sleep 3
  done
  echo "  FAIL: $label after $max attempts" >&2
  docker logs --tail=40 "$container" >&2 || true
  return 1
}
echo "--- health-gating target slot ---"
healthcheck "oet-api-$target_slot" "curl --fail --silent http://127.0.0.1:8080/health/ready" "API ($target_slot)"
healthcheck "oet-web-$target_slot" "wget -qO- http://127.0.0.1:3000/api/health" "WEB ($target_slot)"

# --- flip routers to the target slot ---
echo "--- switching routers to $target_slot ---"
compose "$target_slot" up -d --no-build --force-recreate web learner-api
healthcheck "oet-web" "wget -qO- http://127.0.0.1:3000/api/health" "web router"
healthcheck "oet-api" "wget -qO- http://127.0.0.1:8080/health/ready" "api router"
echo "ACTIVE_SLOT=$target_slot" > .deploy/active-slot.env

# --- public smoke ---
echo "--- public verify ---"
ok=true
curl -sf -m 15 "$APP_PUBLIC_URL/api/health" >/dev/null && echo "  PUBLIC_WEB_OK" || { echo "  PUBLIC_WEB_FAIL" >&2; ok=false; }
curl -sf -m 15 "$API_PUBLIC_URL/health/ready" >/dev/null && echo "  PUBLIC_API_OK" || { echo "  PUBLIC_API_FAIL" >&2; ok=false; }
if [ "$ok" != true ]; then
  echo "[deploy] public gates failed; rolling routers back to $prev_slot" >&2
  compose "$prev_slot" up -d --no-build --force-recreate web learner-api || true
  echo "ACTIVE_SLOT=$prev_slot" > .deploy/active-slot.env
  exit 1
fi

# --- record + keep previous slot warm for instant rollback ---
{
  printf '%s\t%s\tweb=%s\tapi=%s\n' \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$target_slot" "$WEB_IMAGE" "$API_IMAGE"
} >> .deploy/auto-deploy-history.tsv

echo "=== AUTO_DEPLOY_DONE: live on $target_slot (previous slot $prev_slot kept for rollback) ==="
