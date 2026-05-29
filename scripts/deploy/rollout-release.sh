#!/usr/bin/env bash
# Health-gated production rollout from immutable image digests.
set -euo pipefail

APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
APP_PUBLIC_URL="${APP_PUBLIC_URL:-https://app.oetwithdrhesham.co.uk}"
API_PUBLIC_URL="${API_PUBLIC_URL:-https://api.oetwithdrhesham.co.uk}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DRIVER_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$APP_DIR"

require_digest_ref() {
  local key="$1"
  local value="${!key:-}"
  local digest
  case "$value" in
    *@sha256:*) ;;
    *)
      echo "[rollout] $key must be an immutable @sha256 image ref." >&2
      exit 1
      ;;
  esac
  digest="${value##*@sha256:}"
  case "$digest" in
    *[!0-9a-fA-F]*)
      echo "[rollout] $key digest must be hexadecimal." >&2
      exit 1
      ;;
  esac
  if [ "${#digest}" -ne 64 ]; then
    echo "[rollout] $key digest must be 64 hex characters." >&2
    exit 1
  fi
}

for key in WEB_IMAGE API_IMAGE DB_BACKUP_IMAGE ROUTER_IMAGE; do
  require_digest_ref "$key"
done

export WEB_IMAGE API_IMAGE DB_BACKUP_IMAGE ROUTER_IMAGE
export PRODUCTION_IMAGE_PULL_POLICY=always

compose() {
  ACTIVE_SLOT="${ACTIVE_SLOT:-blue}" docker compose --env-file .env.production -f docker-compose.production.yml "$@"
}

healthcheck_container() {
  local container="$1"
  local command="$2"
  local label="$3"
  local attempts="${4:-60}"

  for attempt in $(seq 1 "$attempts"); do
    if docker exec "$container" sh -c "$command" >/dev/null; then
      return 0
    fi
    if [ "$attempt" -eq "$attempts" ]; then
      echo "[rollout] $label health gate failed." >&2
      docker logs --tail=120 "$container" >&2 || true
      return 1
    fi
    sleep 5
  done
}

public_gates() {
  APP_PUBLIC_URL="$APP_PUBLIC_URL" API_PUBLIC_URL="$API_PUBLIC_URL" bash "$SCRIPT_DIR/post-deploy-verify.sh"
  BASE_URL="$APP_PUBLIC_URL" API_BASE_URL="$API_PUBLIC_URL" OBSERVABILITY_SMOKE_OUTPUT="/tmp/observability-smoke-production.json" bash "$DRIVER_ROOT/observability-smoke.sh"
  API_PUBLIC_URL="$API_PUBLIC_URL" bash "$SCRIPT_DIR/reading-media-smoke.sh"
}

verify_image_revision() {
  local key="$1"
  local expected_revision="$2"
  local image="${!key:-}"
  local actual_revision
  actual_revision="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "$image" 2>/dev/null || true)"
  if [ "$actual_revision" != "$expected_revision" ]; then
    echo "[rollout] $key is not labelled for deploy SHA $expected_revision." >&2
    echo "[rollout] image: $image" >&2
    echo "[rollout] org.opencontainers.image.revision: ${actual_revision:-<missing>}" >&2
    exit 1
  fi
}

mkdir -p .deploy
previous_sha=""
previous_slot=""
if [ -s .deploy/previous-good.env ]; then
  previous_sha=$(awk -F= '$1 == "PREVIOUS_GOOD_SHA" { print $2 }' .deploy/previous-good.env | tail -n 1)
  cp .deploy/previous-good.env .deploy/rollback-target.env
fi
if [ -s .deploy/active-slot.env ]; then
  previous_slot=$(awk -F= '$1 == "ACTIVE_SLOT" { print $2 }' .deploy/active-slot.env | tail -n 1)
fi

case "$previous_slot" in
  blue) target_slot="green" ;;
  green) target_slot="blue" ;;
  "") target_slot="blue" ;;
  *)
    echo "[rollout] invalid active slot in .deploy/active-slot.env: $previous_slot" >&2
    exit 1
    ;;
esac

echo "[rollout] active slot before rollout: ${previous_slot:-none}; target slot: $target_slot"

echo "[rollout] validating production env"
bash "$SCRIPT_DIR/validate-production-env.sh" .env.production
bash "$SCRIPT_DIR/mock-stub-scan.sh" .env.production

echo "[rollout] pulling immutable images"
ACTIVE_SLOT="$target_slot" compose pull "learner-api-$target_slot" "web-$target_slot" learner-api web db-backup
deploy_sha="$(git rev-parse HEAD)"
for key in WEB_IMAGE API_IMAGE DB_BACKUP_IMAGE ROUTER_IMAGE; do
  verify_image_revision "$key" "$deploy_sha"
done

echo "[rollout] starting target slot from digest images"
ACTIVE_SLOT="$target_slot" compose up -d --no-build --force-recreate "learner-api-$target_slot" "web-$target_slot" db-backup

echo "[rollout] waiting for target slot health"
api_container=$(ACTIVE_SLOT="$target_slot" compose ps -q "learner-api-$target_slot")
web_container=$(ACTIVE_SLOT="$target_slot" compose ps -q "web-$target_slot")
if [ -z "$api_container" ] || [ -z "$web_container" ]; then
  echo "[rollout] failed to resolve target slot containers." >&2
  exit 1
fi

healthcheck_container "$api_container" "curl --fail --silent --show-error http://127.0.0.1:8080/health/ready" "target API readiness"
healthcheck_container "$web_container" "wget -qO- http://127.0.0.1:3000/api/health" "target web health"

echo "[rollout] switching stable routers to $target_slot"
ACTIVE_SLOT="$target_slot" compose up -d --no-build --force-recreate learner-api web

api_router=$(ACTIVE_SLOT="$target_slot" compose ps -q learner-api)
web_router=$(ACTIVE_SLOT="$target_slot" compose ps -q web)
if [ -z "$api_router" ] || [ -z "$web_router" ]; then
  echo "[rollout] failed to resolve router containers." >&2
  exit 1
fi

healthcheck_container "$api_router" "wget -qO- http://127.0.0.1:8080/health/ready" "API router"
healthcheck_container "$web_router" "wget -qO- http://127.0.0.1:3000/api/health" "web router"

echo "[rollout] running public post-deploy gates"
if ! public_gates; then
  echo "[rollout] public gates failed after traffic switch." >&2
  if [ -n "$previous_slot" ]; then
    echo "[rollout] rolling stable routers back to $previous_slot" >&2
    ACTIVE_SLOT="$previous_slot" compose up -d --no-build --force-recreate learner-api web
    api_router=$(ACTIVE_SLOT="$previous_slot" compose ps -q learner-api)
    web_router=$(ACTIVE_SLOT="$previous_slot" compose ps -q web)
    if [ -z "$api_router" ] || [ -z "$web_router" ]; then
      echo "[rollout] SEV-1: failed to resolve router containers while rolling back to $previous_slot." >&2
      exit 1
    fi
    healthcheck_container "$api_router" "wget -qO- http://127.0.0.1:8080/health/ready" "rollback API router"
    healthcheck_container "$web_router" "wget -qO- http://127.0.0.1:3000/api/health" "rollback web router"
  fi
  exit 1
fi

current_sha="$deploy_sha"
{
  echo "PREVIOUS_GOOD_SHA=$current_sha"
  echo "PREVIOUS_GOOD_SLOT=$target_slot"
  echo "PREVIOUS_GOOD_WEB_IMAGE=$WEB_IMAGE"
  echo "PREVIOUS_GOOD_API_IMAGE=$API_IMAGE"
  echo "PREVIOUS_GOOD_DB_BACKUP_IMAGE=$DB_BACKUP_IMAGE"
  echo "PREVIOUS_GOOD_ROUTER_IMAGE=$ROUTER_IMAGE"
  echo "PREVIOUS_GOOD_RECORDED_AT_UTC=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  if [ -n "$previous_sha" ]; then
    echo "ROLLED_FROM_SHA=$previous_sha"
    echo "ROLLED_FROM_SLOT=$previous_slot"
  fi
} > .deploy/previous-good.env

{
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    "$current_sha" \
    "$target_slot" \
    "$WEB_IMAGE" \
    "$API_IMAGE" \
    "$DB_BACKUP_IMAGE" \
    "$ROUTER_IMAGE"
} >> .deploy/release-history.tsv

echo "ACTIVE_SLOT=$target_slot" > .deploy/active-slot.env

if [ -n "$previous_slot" ] && [ "${KEEP_PREVIOUS_SLOT_RUNNING:-true}" != "true" ]; then
  echo "[rollout] stopping previous slot because KEEP_PREVIOUS_SLOT_RUNNING is false: $previous_slot"
  ACTIVE_SLOT="$target_slot" compose stop "learner-api-$previous_slot" "web-$previous_slot"
fi

echo "[rollout] release is healthy and recorded as previous-good: $current_sha on $target_slot"
