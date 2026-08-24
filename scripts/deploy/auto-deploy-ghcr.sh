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
COMPOSE_FILE="${VPS_COMPOSE_FILE:-$APP_DIR/docker-compose.production.yml}"
VALIDATE_ENV_SCRIPT="${VPS_VALIDATE_ENV_SCRIPT:-$APP_DIR/scripts/deploy/validate-production-env.sh}"
PROTECT_SCRIPT="${VPS_PROTECT_SCRIPT:-$APP_DIR/scripts/deploy/protect-production-data.sh}"
APP_PUBLIC_URL="${APP_PUBLIC_URL:-https://app.oetwithdrhesham.co.uk}"
API_PUBLIC_URL="${API_PUBLIC_URL:-https://api.oetwithdrhesham.co.uk}"
: "${WEB_IMAGE:?Set WEB_IMAGE to the GHCR web image ref}"
: "${API_IMAGE:?Set API_IMAGE to the GHCR api image ref}"
: "${DB_BACKUP_IMAGE:?Set DB_BACKUP_IMAGE to the GHCR backup image ref}"
# The Antigravity agent gateway is a shared (non-slotted) service. Optional for
# rollback compatibility: when unset the gateway container is left untouched.
: "${AGENT_GATEWAY_IMAGE:=}"
cd "$APP_DIR"
export VPS_APP_DIR

echo "=== AUTO_DEPLOY_START $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
echo "WEB_IMAGE=$WEB_IMAGE"
echo "API_IMAGE=$API_IMAGE"
[ -n "$AGENT_GATEWAY_IMAGE" ] && echo "AGENT_GATEWAY_IMAGE=$AGENT_GATEWAY_IMAGE"

mkdir -p /var/opt/oet-learner/releases
if [ -n "${NGINX_TEMPLATE_SRC:-}" ] && [ -f "$NGINX_TEMPLATE_SRC" ]; then
  mkdir -p "$APP_DIR/scripts/deploy/nginx"
  cp -f "$NGINX_TEMPLATE_SRC" "$APP_DIR/scripts/deploy/nginx/web-bluegreen.conf.template"
fi
if [ "$COMPOSE_FILE" != "$APP_DIR/docker-compose.production.yml" ] && [ -f "$COMPOSE_FILE" ]; then
  cp -f "$COMPOSE_FILE" "$APP_DIR/docker-compose.production.yml"
fi
if ! grep -q '^RELEASES_HOST_PATH=' .env.production 2>/dev/null; then
  echo 'RELEASES_HOST_PATH=/var/opt/oet-learner/releases' >> .env.production
fi

echo "--- validating production env ---"
bash "$VALIDATE_ENV_SCRIPT" .env.production

if [ -f "$PROTECT_SCRIPT" ]; then
  echo "--- installing production data protection ---"
  bash "$PROTECT_SCRIPT"
fi

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
for kv in "WEB_IMAGE=$WEB_IMAGE" "API_IMAGE=$API_IMAGE" "DB_BACKUP_IMAGE=$DB_BACKUP_IMAGE" \
  ${AGENT_GATEWAY_IMAGE:+""}; do
  key="${kv%%=*}"
  if grep -q "^${key}=" .env.production 2>/dev/null; then
    sed -i "s#^${key}=.*#${kv}#" .env.production
  else
    echo "$kv" >> .env.production
  fi
done
if [ -n "$AGENT_GATEWAY_IMAGE" ]; then
  key="AGENT_GATEWAY_IMAGE"
  if grep -q "^${key}=" .env.production 2>/dev/null; then
    sed -i "s#^${key}=.*#${key}=${AGENT_GATEWAY_IMAGE}#" .env.production
  else
    echo "${key}=${AGENT_GATEWAY_IMAGE}" >> .env.production
  fi
fi

export WEB_IMAGE API_IMAGE DB_BACKUP_IMAGE AGENT_GATEWAY_IMAGE
compose() {
  local slot="$1"
  shift
  local joined
  joined=" $* "
  if [[ "$joined" == *" down "* ]] && { [[ "$joined" == *" -v "* ]] || [[ "$joined" == *" --volumes "* ]]; }; then
    echo "REFUSING: compose down must never remove volumes" >&2
    return 99
  fi
  ACTIVE_SLOT="$slot" docker compose --env-file "$APP_DIR/.env.production" -f "$COMPOSE_FILE" "$@"
}

# --- pull the freshly-built images (no build here) ---
# ghcr.io pulls over the shared VPS link intermittently drop mid-transfer with
# "read tcp ... connection reset by peer". The build+push jobs already succeeded,
# so the image IS in the registry — a reset is transient. Retry with backoff
# instead of failing the whole deploy (this step was flaking ~4 of 5 deploys and
# needed a manual `gh run rerun --failed` each time).
pull_with_retry() {
  local image="$1" attempts="${2:-5}" delay=5 i
  for i in $(seq 1 "$attempts"); do
    if docker pull "$image"; then
      return 0
    fi
    if [ "$i" -lt "$attempts" ]; then
      echo "  [pull] attempt $i/$attempts failed for $image; retrying in ${delay}s..." >&2
      sleep "$delay"
      delay=$(( delay < 30 ? delay * 2 : 30 ))
    fi
  done
  echo "  [pull] FAILED to pull $image after $attempts attempts" >&2
  return 1
}

echo "--- pulling images ---"
pull_with_retry "$WEB_IMAGE"
pull_with_retry "$API_IMAGE"
pull_with_retry "$DB_BACKUP_IMAGE"
if [ -n "$AGENT_GATEWAY_IMAGE" ]; then
  pull_with_retry "$AGENT_GATEWAY_IMAGE"
fi

# Recreate ONLY the inactive web/API slot + backup sidecar.
# Never recreate postgres. Never pass -v. Named volumes stay mounted.
echo "--- starting target slot ($target_slot) ---"
compose "$target_slot" up -d --no-build --force-recreate \
  "web-$target_slot" "learner-api-$target_slot" db-backup agent-gateway

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
if [ -n "$AGENT_GATEWAY_IMAGE" ]; then
  # Liveness only: /v1/readyz stays 503 when GEMINI_API_KEY is empty.
  # Do not invent a key; web/API must still promote in that degraded state.
  healthcheck "oet-agent-gateway" "wget -qO- http://127.0.0.1:8305/v1/healthz" "AGENT GATEWAY"
fi

# --- flip routers to the target slot ---
echo "--- switching routers to $target_slot ---"
router_switch_with_retry() {
  local attempts=3 delay=10 attempt
  for attempt in $(seq 1 "$attempts"); do
    if compose "$target_slot" up -d --no-build --force-recreate web learner-api; then
      return 0
    fi
    if [ "$attempt" -lt "$attempts" ]; then
      echo "  [router] attempt $attempt/$attempts failed; retrying in ${delay}s..." >&2
      sleep "$delay"
    fi
  done
  echo "  [router] failed to switch routers after $attempts attempts" >&2
  return 1
}
router_switch_with_retry
healthcheck "oet-web" "wget -qO- http://127.0.0.1:3000/api/health" "web router"
healthcheck "oet-api" "wget -qO- http://127.0.0.1:8080/health/ready" "api router"
echo "ACTIVE_SLOT=$target_slot" > .deploy/active-slot.env

# --- public smoke ---
publiccheck() {
  local url="$1" label="$2" max="${3:-12}"
  for i in $(seq 1 "$max"); do
    if curl -sf -m 15 "$url" >/dev/null; then
      echo "  OK: $label (attempt $i)"; return 0
    fi
    sleep 5
  done
  echo "  FAIL: $label after $max attempts" >&2
  return 1
}
echo "--- public verify ---"
ok=true
publiccheck "$APP_PUBLIC_URL/api/health" "public web" || ok=false
publiccheck "$API_PUBLIC_URL/health/ready" "public api" || ok=false
if [ "$ok" != true ]; then
  echo "[deploy] public gates failed; rolling routers back to $prev_slot" >&2
  compose "$prev_slot" up -d --no-build --force-recreate web learner-api || true
  echo "ACTIVE_SLOT=$prev_slot" > .deploy/active-slot.env
  exit 1
fi

# --- record + keep previous slot warm for instant rollback ---
{
  printf '%s\t%s\tweb=%s\tapi=%s' \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$target_slot" "$WEB_IMAGE" "$API_IMAGE"
  [ -n "$AGENT_GATEWAY_IMAGE" ] && printf '\tagent-gateway=%s' "$AGENT_GATEWAY_IMAGE"
  printf '\n'
} >> .deploy/auto-deploy-history.tsv

echo "=== AUTO_DEPLOY_DONE: live on $target_slot (previous slot $prev_slot kept for rollback) ==="
