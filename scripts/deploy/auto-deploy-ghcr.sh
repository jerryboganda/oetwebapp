#!/usr/bin/env bash
# Memory-safe fresh deploy from pre-built GHCR images (NO build on the VPS).
#
# The prod VPS is a shared host (60+ co-tenant containers); building Next.js
# in-place OOM-cascades the whole box. So images are built off-box in CI
# (.github/workflows/build-images.yml + production-deploy.yml) and this script only PULLS + recreates
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
: "${RELEASE_SHA:=${WEB_IMAGE##*:}}"
: "${DEPLOY_PHASE:=all}"
[[ "$RELEASE_SHA" =~ ^[a-f0-9]{40}$ ]] || { echo "Invalid release SHA" >&2; exit 1; }
case "$DEPLOY_PHASE" in prepare|promote|all) ;; *) echo "Invalid deploy phase" >&2; exit 1 ;; esac
started_ms="$(date +%s%3N)"
phase_ms="$started_ms"
phase_done() {
  local now
  now="$(date +%s%3N)"
  echo "DEPLOY_PHASE name=$1 milliseconds=$((now - phase_ms)) sha=$RELEASE_SHA"
  phase_ms="$now"
}
cd "$APP_DIR"
export VPS_APP_DIR

echo "=== AUTO_DEPLOY_START $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
echo "WEB_IMAGE=$WEB_IMAGE"
echo "API_IMAGE=$API_IMAGE"
[ -n "$AGENT_GATEWAY_IMAGE" ] && echo "AGENT_GATEWAY_IMAGE=$AGENT_GATEWAY_IMAGE"

mkdir -p /var/opt/oet-learner/releases
mkdir -p .deploy
if [ ! -s .deploy/live-router-compose.yml ] && [ -s "$APP_DIR/docker-compose.production.yml" ]; then
  cp "$APP_DIR/docker-compose.production.yml" .deploy/live-router-compose.yml
  chmod 600 .deploy/live-router-compose.yml
fi
if [ -n "${NGINX_TEMPLATE_SRC:-}" ] && [ -f "$NGINX_TEMPLATE_SRC" ]; then
  mkdir -p "$APP_DIR/scripts/deploy/nginx"
  cp -f "$NGINX_TEMPLATE_SRC" "$APP_DIR/scripts/deploy/nginx/web-bluegreen.conf.template"
fi
# The API router (docker-compose.production.yml's learner-api service) bind-mounts
# this same directory's api-bluegreen.conf.template. It was never synced here (only
# the web template was), so an edit to it in the repo silently never reached
# production -- the file on the VPS was whatever had been placed there once, by hand.
if [ -n "${NGINX_API_TEMPLATE_SRC:-}" ] && [ -f "$NGINX_API_TEMPLATE_SRC" ]; then
  mkdir -p "$APP_DIR/scripts/deploy/nginx"
  cp -f "$NGINX_API_TEMPLATE_SRC" "$APP_DIR/scripts/deploy/nginx/api-bluegreen.conf.template"
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
if [ "$(docker inspect -f '{{.State.Running}}' oet-web 2>/dev/null || true)" = "true" ]; then
  routed_slot="$(docker exec oet-web nginx -T 2>/dev/null | sed -nE 's/.*http:\/\/web-(blue|green):3000.*/\1/p' | sort -u)"
  [[ "$routed_slot" =~ ^(blue|green)$ ]] || { echo "Cannot prove current web routing" >&2; exit 1; }
  api_slot="$(docker exec oet-api nginx -T 2>/dev/null | sed -nE 's/.*http:\/\/learner-api-(blue|green):8080.*/\1/p' | sort -u)"
  [ "$routed_slot" = "$api_slot" ] || { echo "Current router pair disagrees" >&2; exit 1; }
  prev_slot="$routed_slot"
  if [ "$prev_slot" = "blue" ]; then target_slot=green; else target_slot=blue; fi
fi
echo "active slot: ${prev_slot:-none} -> deploying to: $target_slot"

# --- persist image refs so any future manual compose op uses them too ---
mkdir -p .deploy
for kv in "WEB_IMAGE=$WEB_IMAGE" "API_IMAGE=$API_IMAGE" "DB_BACKUP_IMAGE=$DB_BACKUP_IMAGE"; do
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
export PRODUCTION_IMAGE_PULL_POLICY=never
# Also supports proven older releases whose Compose file still mounts startup
# templates. Rendered directories survive nginx/container/host restarts.
cat > .deploy/router-compose.yml <<'ROUTER_COMPOSE'
services:
  web:
    environment:
      ACTIVE_SLOT: blue
      NGINX_ENVSUBST_OUTPUT_DIR: /tmp/oet-unused-templates
    volumes:
      - ${VPS_APP_DIR}/.deploy/nginx/web:/etc/nginx/conf.d:ro
  learner-api:
    environment:
      ACTIVE_SLOT: blue
      NGINX_ENVSUBST_OUTPUT_DIR: /tmp/oet-unused-templates
    volumes:
      - ${VPS_APP_DIR}/.deploy/nginx/api:/etc/nginx/conf.d:ro
ROUTER_COMPOSE
compose() {
  local slot="$1"
  shift
  local joined
  joined=" $* "
  if [[ "$joined" == *" down "* ]] && { [[ "$joined" == *" -v "* ]] || [[ "$joined" == *" --volumes "* ]]; }; then
    echo "REFUSING: compose down must never remove volumes" >&2
    return 99
  fi
  ACTIVE_SLOT="$slot" docker compose --project-directory "$APP_DIR" --env-file "$APP_DIR/.env.production" \
    -f "$COMPOSE_FILE" -f "$APP_DIR/.deploy/router-compose.yml" "$@"
}

# --- pull the freshly-built images (no build here) ---
# ghcr.io pulls over the shared VPS link intermittently drop mid-transfer with
# "read tcp ... connection reset by peer". The build+push jobs already succeeded,
# so the image IS in the registry — a reset is transient. Retry with backoff
# instead of failing the whole deploy (this step was flaking ~4 of 5 deploys and
# needed a manual `gh run rerun --failed` each time).
pull_with_retry() {
  local image="$1" attempts="${2:-5}" delay=5 i
  if [[ "$image" == *@sha256:* ]] && docker image inspect "$image" >/dev/null 2>&1; then
    echo "  [pull] immutable image already present: $image"
    return 0
  fi
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

if [ "$DEPLOY_PHASE" != "promote" ]; then
echo "--- pulling images (in parallel) ---"
# Parallel pulls (owner directive 2026-10-03: minimum deploy time): the four
# images are independent and the link is latency-bound, so pulling them at once
# roughly halves the wait. Each pull keeps its own retry/backoff and any
# failure still aborts the rollout before a slot is touched.
pull_pids=()
pull_labels=()
start_pull() {
  pull_labels+=("$1")
  pull_with_retry "$2" &
  pull_pids+=("$!")
}
start_pull web "$WEB_IMAGE"
start_pull api "$API_IMAGE"
start_pull db-backup "$DB_BACKUP_IMAGE"
if [ -n "$AGENT_GATEWAY_IMAGE" ]; then
  start_pull agent-gateway "$AGENT_GATEWAY_IMAGE"
fi

pull_failed=""
for i in "${!pull_pids[@]}"; do
  if ! wait "${pull_pids[$i]}"; then
    pull_failed="$pull_failed ${pull_labels[$i]}"
  fi
done
if [ -n "$pull_failed" ]; then
  echo "  [pull] FAILED:$pull_failed" >&2
  exit 1
fi
for image in "$WEB_IMAGE" "$API_IMAGE" "$DB_BACKUP_IMAGE" "$AGENT_GATEWAY_IMAGE"; do
  [ -n "$image" ] || continue
  repository="${image%@*}"
  repository="${repository%:*}"
  docker tag "$image" "${repository}:$RELEASE_SHA"
done
phase_done image_pull

# The API slots join the internal-only network shared with the separate
# oet-agent-console compose project (docker-compose.production.yml declares it
# external). Create it on first use so a main deploy never depends on the
# console workflow having run first.
echo "--- ensuring internal network oet_agent_ctl ---"
docker network inspect oet_agent_ctl >/dev/null 2>&1 || docker network create --internal oet_agent_ctl
if [ "$(docker network inspect -f '{{.Internal}}' oet_agent_ctl 2>/dev/null || true)" != "true" ]; then
  echo "  WARNING: oet_agent_ctl exists but is not --internal; the owner-agent workflow will refuse to use it" >&2
fi

# The API slots also join the plain bridge network shared with the GEPA
# placement engine stack (docker-compose.production.yml declares it external).
# Create it on first use so a main deploy never depends on the GEPA compose
# project having run first. NOT --internal: matches the engine stack's network.
echo "--- ensuring placement engine network platform ---"
docker network inspect platform >/dev/null 2>&1 || docker network create platform

# Recreate ONLY the inactive web/API slot + backup sidecar.
# Never recreate postgres. Never pass -v. Named volumes stay mounted.
# --no-deps: compose would otherwise also recreate a *dependency* (postgres,
# clamav) whose config diverged from the file (e.g. the postgres logging block),
# restarting the production database mid-deploy.
echo "--- starting target slot ($target_slot) ---"
service_matches() {
  local service="$1" container="$2" desired_image="$3" desired_hash actual_hash running actual_image health
  docker container inspect "$container" >/dev/null 2>&1 || return 1
  desired_hash="$(compose "$target_slot" config --hash "$service" | awk '{print $NF}')" || return 2
  actual_hash="$(docker inspect -f '{{index .Config.Labels "com.docker.compose.config-hash"}}' "$container")" || return 2
  [ "$desired_hash" = "$actual_hash" ] || return 1
  running="$(docker inspect -f '{{.State.Running}}' "$container")" || return 2
  [ "$running" = "true" ] || return 1
  actual_image="$(docker inspect -f '{{.Image}}' "$container")" || return 2
  desired_image="$(docker image inspect -f '{{.Id}}' "$desired_image")" || return 2
  [ "$actual_image" = "$desired_image" ] || return 1
  health="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container")" || return 2
  [ "$health" = "healthy" ] || [ "$health" = "none" ]
}
update_services=()
for pair in "web-$target_slot:oet-web-$target_slot" "learner-api-$target_slot:oet-api-$target_slot" \
  "db-backup:oet-db-backup" "ai-worker:oet-ai-worker" "agent-gateway:oet-agent-gateway"; do
  service="${pair%%:*}"; container="${pair#*:}"
  [ "$service" != "agent-gateway" ] || [ -n "$AGENT_GATEWAY_IMAGE" ] || continue
  case "$service" in
    web-*) image="$WEB_IMAGE" ;;
    learner-api-*|ai-worker) image="$API_IMAGE" ;;
    db-backup) image="$DB_BACKUP_IMAGE" ;;
    agent-gateway) image="$AGENT_GATEWAY_IMAGE" ;;
  esac
  if service_matches "$service" "$container" "$image"; then
    echo "DEPLOY_REUSE service=$service"
  else
    status=$?
    [ "$status" -eq 1 ] || { echo "Cannot inspect effective configuration: $service" >&2; exit "$status"; }
    update_services+=("$service")
  fi
done
if [ "${#update_services[@]}" -gt 0 ]; then
  # Compose retains the worker's 90s and gateway's 45s graceful stop periods.
  compose "$target_slot" up -d --no-build --no-deps --pull never --force-recreate "${update_services[@]}"
fi
phase_done service_update
fi

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
health_pids=()
healthcheck "oet-api-$target_slot" "curl --fail --silent http://127.0.0.1:8080/health/ready" "API ($target_slot)" &
health_pids+=("$!")
healthcheck "oet-web-$target_slot" "wget -qO- http://127.0.0.1:3000/api/health" "WEB ($target_slot)" &
health_pids+=("$!")
healthcheck "oet-ai-worker" "curl --fail --silent http://127.0.0.1:8080/health/live" "AI WORKER" &
health_pids+=("$!")
if [ -n "$AGENT_GATEWAY_IMAGE" ]; then
  # Liveness only: /v1/readyz stays 503 when GEMINI_API_KEY is empty.
  # Do not invent a key; web/API must still promote in that degraded state.
  healthcheck "oet-agent-gateway" "wget -qO- http://127.0.0.1:8305/v1/healthz" "AGENT GATEWAY" &
  health_pids+=("$!")
fi
health_failed=false
for pid in "${health_pids[@]}"; do wait "$pid" || health_failed=true; done
[ "$health_failed" = false ] || exit 1
phase_done target_ready

identity="$(printf '%s\n' "$RELEASE_SHA" "$prev_slot" "$target_slot" "$WEB_IMAGE" "$API_IMAGE" \
  "$DB_BACKUP_IMAGE" "$AGENT_GATEWAY_IMAGE" "$(compose "$target_slot" config | sha256sum)" \
  "$(sha256sum "$APP_DIR/scripts/deploy/nginx/"*-bluegreen.conf.template)" | sha256sum | cut -d' ' -f1)"
pending=".deploy/prepared-$RELEASE_SHA"
if [ "$DEPLOY_PHASE" = "prepare" ]; then
  printf '%s\n%s\n' "$identity" "$started_ms" > "$pending"
  echo "DEPLOY_PREPARED sha=$RELEASE_SHA slot=$target_slot"
  exit 0
elif [ "$DEPLOY_PHASE" = "promote" ]; then
  [ -s "$pending" ] && [ "$(head -n1 "$pending")" = "$identity" ] || { echo "Prepared release identity mismatch" >&2; exit 1; }
  started_ms="$(sed -n '2p' "$pending")"
  [[ "$started_ms" =~ ^[0-9]{13,}$ ]] || { echo "Invalid preparation timestamp" >&2; exit 1; }
fi

# --- flip routers to the target slot ---
echo "--- switching routers to $target_slot ---"
render_router() {
  local template="$1" slot="$2" output="$3"
  awk -v slot="$slot" -v sha="$RELEASE_SHA" '
    { gsub(/\$\{ACTIVE_SLOT\}/, slot); print }
    /^[[:space:]]*server_name[[:space:]]/ {
      printf "    add_header X-Oet-Release \"%s\" always;\n", sha
      printf "    add_header X-Oet-Slot \"%s\" always;\n", slot
    }
  ' "$template" > "$output"
}
mkdir -p .deploy/nginx/web .deploy/nginx/api
for kind in web api; do
  config=".deploy/nginx/$kind/default.conf"
  if [ ! -s "$config" ]; then
    if docker container inspect "oet-$kind" >/dev/null 2>&1; then
      docker exec "oet-$kind" cat /etc/nginx/conf.d/default.conf > "$config"
    else
      render_router "$APP_DIR/scripts/deploy/nginx/$kind-bluegreen.conf.template" "$prev_slot" "$config"
    fi
  fi
  cp "$config" ".deploy/nginx/$kind/previous.conf.saved"
  render_router "$APP_DIR/scripts/deploy/nginx/$kind-bluegreen.conf.template" "$target_slot" ".deploy/nginx/$kind/candidate.conf.saved"
done
# Native recreation only when router image/mounts/effective settings changed.
previous_router_images=()
for kind in web api; do
  if docker container inspect "oet-$kind" >/dev/null 2>&1; then
    previous_router_images+=("$kind=$(docker inspect -f '{{.Image}}' "oet-$kind")")
  fi
done
cutover_started=false
router_start_failed=false
rollback_routers() {
  local failed=false kind pair service recover_images=false previous actual
  for kind in web api; do
    cp ".deploy/nginx/$kind/previous.conf.saved" ".deploy/nginx/$kind/restore.conf.saved" || failed=true
    mv ".deploy/nginx/$kind/restore.conf.saved" ".deploy/nginx/$kind/default.conf" || failed=true
  done
  for pair in "${previous_router_images[@]}"; do
    kind="${pair%%=*}"; previous="${pair#*=}"
    actual="$(docker inspect -f '{{.Image}}' "oet-$kind" 2>/dev/null || true)"
    [ "$previous" = "$actual" ] || recover_images=true
  done
  if [ "${#previous_router_images[@]}" -eq 2 ] && { [ "$router_start_failed" = true ] || [ "$recover_images" = true ]; }; then
    {
      echo "services:"
      for pair in "${previous_router_images[@]}"; do
        kind="${pair%%=*}"
        if [ "$kind" = api ]; then service=learner-api; else service=web; fi
        printf '  %s:\n    image: "%s"\n' "$service" "${pair#*=}"
      done
    } > .deploy/router-rollback.yml
    [ -s .deploy/live-router-compose.yml ] || { echo "Previous router runtime definition is missing" >&2; return 1; }
    ACTIVE_SLOT="$prev_slot" docker compose --project-directory "$APP_DIR" --env-file "$APP_DIR/.env.production" \
      -f "$APP_DIR/.deploy/live-router-compose.yml" -f "$APP_DIR/.deploy/router-compose.yml" -f "$APP_DIR/.deploy/router-rollback.yml" \
      up -d --no-build --no-deps --pull never web learner-api || failed=true
  fi
  for kind in web api; do
    docker exec "oet-$kind" nginx -t && docker exec "oet-$kind" nginx -s reload || failed=true
  done
  healthcheck oet-web "wget -qO- http://127.0.0.1:3000/api/health" "restored web router" || failed=true
  healthcheck oet-api "wget -qO- http://127.0.0.1:8080/health/ready" "restored api router" || failed=true
  if [ "$failed" = true ]; then echo "Paired router rollback FAILED" >&2; return 1; fi
  echo "ACTIVE_SLOT=$prev_slot" > .deploy/active-slot.env
  echo "DEPLOY_ROLLBACK slot=$prev_slot" >&2
}
cutover_failure() {
  local status=$?
  trap - EXIT
  if [ "$status" -ne 0 ] && [ "$cutover_started" = true ]; then rollback_routers || status=1; fi
  exit "$status"
}
trap cutover_failure EXIT
router_image="$(compose "$prev_slot" config --images web | head -n1)"
pull_with_retry "$router_image"
cutover_started=true
if ! compose "$prev_slot" up -d --no-build --no-deps --pull never web learner-api; then
  router_start_failed=true
  exit 1
fi
for kind in web api; do
  docker cp ".deploy/nginx/$kind/candidate.conf.saved" "oet-$kind:/tmp/oet-candidate.conf"
  docker exec "oet-$kind" sh -c \
    'set -e; sed "s@/etc/nginx/conf.d/\*.conf@/tmp/oet-candidate.conf@" /etc/nginx/nginx.conf > /tmp/oet-candidate-main.conf; grep -Fq /tmp/oet-candidate.conf /tmp/oet-candidate-main.conf; nginx -t -c /tmp/oet-candidate-main.conf'
done
for kind in web api; do
  mv ".deploy/nginx/$kind/candidate.conf.saved" ".deploy/nginx/$kind/default.conf"
done
for kind in web api; do
  docker exec "oet-$kind" nginx -t
  docker exec "oet-$kind" nginx -s reload
done
healthcheck "oet-web" "wget -qO- http://127.0.0.1:3000/api/health" "web router"
healthcheck "oet-api" "wget -qO- http://127.0.0.1:8080/health/ready" "api router"
phase_done router_cutover

# --- public smoke ---
publiccheck() {
  local url="$1" label="$2" max="${3:-12}" status
  for i in $(seq 1 "$max"); do
    if status="$(curl -sf -m 15 -D ".deploy/public-$label.headers" -o /dev/null -w '%{http_code}' "$url")" \
      && [ "$status" = 200 ] \
      && tr -d '\r' < ".deploy/public-$label.headers" | grep -Eiq "^X-Oet-Release: *$RELEASE_SHA$" \
      && tr -d '\r' < ".deploy/public-$label.headers" | grep -Eiq "^X-Oet-Slot: *$target_slot$"; then
      echo "  OK: $label (attempt $i)"; return 0
    fi
    sleep 5
  done
  echo "  FAIL: $label after $max attempts" >&2
  return 1
}
echo "--- public verify ---"
ok=true
publiccheck "$APP_PUBLIC_URL/api/health" "web" || ok=false
publiccheck "$API_PUBLIC_URL/health/ready" "api-ready" || ok=false
publiccheck "$API_PUBLIC_URL/health/live" "api-live" || ok=false
if [ "$ok" != true ]; then
  echo "[deploy] public gates failed; rolling routers back to $prev_slot" >&2
  exit 1
fi
for pair in "web:oet-web-$target_slot" "api:oet-api-$target_slot"; do
  kind="${pair%%:*}"; container="${pair#*:}"
  if [ "$kind" = web ]; then expected="$WEB_IMAGE"; else expected="$API_IMAGE"; fi
  [ "$(docker inspect -f '{{.Image}}' "$container")" = "$(docker image inspect -f '{{.Id}}' "$expected")" ] \
    || { echo "Serving image mismatch: $container" >&2; exit 1; }
done
phase_done public_health_and_revision
echo "ACTIVE_SLOT=$target_slot" > .deploy/active-slot.env
{
  printf 'RELEASE_SHA=%s\nACTIVE_SLOT=%s\nWEB_IMAGE=%s\nAPI_IMAGE=%s\n' "$RELEASE_SHA" "$target_slot" "$WEB_IMAGE" "$API_IMAGE"
} > .deploy/live-release.env
live_ms="$(date +%s%3N)"
echo "DEPLOY_LIVE sha=$RELEASE_SHA slot=$target_slot live_at=$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ) rollout_milliseconds=$((live_ms - started_ms))"
cutover_started=false
cp "$COMPOSE_FILE" .deploy/live-router-compose.next
chmod 600 .deploy/live-router-compose.next
mv .deploy/live-router-compose.next .deploy/live-router-compose.yml
rm -f "$pending" .deploy/public-web.headers .deploy/public-api-ready.headers .deploy/public-api-live.headers

# --- record + keep previous slot warm for instant rollback ---
{
  printf '%s\t%s\tweb=%s\tapi=%s' \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$target_slot" "$WEB_IMAGE" "$API_IMAGE"
  [ -n "$AGENT_GATEWAY_IMAGE" ] && printf '\tagent-gateway=%s' "$AGENT_GATEWAY_IMAGE"
  printf '\n'
} >> .deploy/auto-deploy-history.tsv

echo "=== AUTO_DEPLOY_DONE: live on $target_slot (previous slot $prev_slot kept for rollback) ==="
