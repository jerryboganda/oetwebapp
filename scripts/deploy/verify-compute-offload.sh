#!/usr/bin/env bash
# Static deployment contract for the production compute-offload path:
# GitHub Actions builds, tests and generates migrations; the VPS only pulls
# pre-built images and runs data/runtime gates.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
# Owner directive 2026-10-03: the former deploy.yml is split so builds run in
# parallel per SHA while production rollouts serialize. Migrations are still
# generated on Actions (build-images.yml) and applied by the deploy workflow
# (production-deploy.yml) - which only starts after the whole build run passed,
# so prod can never migrate ahead of a failed build.
BUILD_WORKFLOW="$REPO_ROOT/.github/workflows/build-images.yml"
DEPLOY_WORKFLOW="$REPO_ROOT/.github/workflows/production-deploy.yml"
ROLLOUT="$SCRIPT_DIR/auto-deploy-ghcr.sh"
IMMUTABLE_ROLLOUT="$SCRIPT_DIR/rollout-release.sh"
PREFLIGHT="$SCRIPT_DIR/pre-flight.sh"

require_literal() {
  local file="$1"
  local literal="$2"
  if ! grep -Fq -- "$literal" "$file"; then
    echo "[compute-offload] missing required contract in $file: $literal" >&2
    exit 1
  fi
}

require_match() {
  local pattern="$1"
  local file="$2"
  local message="$3"
  if ! grep -Eq -- "$pattern" "$file"; then
    echo "[compute-offload] $message" >&2
    exit 1
  fi
}

require_literal "$BUILD_WORKFLOW" 'migrations script --idempotent --no-build --configuration Release'
require_literal "$BUILD_WORKFLOW" 'api-release-${{ github.sha }}'
require_literal "$DEPLOY_WORKFLOW" 'release-manifest.mjs verify-api'
require_literal "$DEPLOY_WORKFLOW" 'apply-migrations-from-ci.sh'
require_literal "$ROLLOUT" '--no-build'
require_literal "$ROLLOUT" 'DB_BACKUP_IMAGE'
require_literal "$IMMUTABLE_ROLLOUT" '--no-build'
require_literal "$PREFLIGHT" '--no-build'
# SQL reuses the API publish compilation; only verified successful-build
# artifacts may be applied, before the new API is started.
require_match '^  apply-migrations:' "$DEPLOY_WORKFLOW" \
  "production-deploy must keep its verified SQL application gate."
require_match "needs.resolve.outputs.api_changed == 'true'" "$DEPLOY_WORKFLOW" \
  "migrations must run when the API digest differs from proven deployed provenance."
if grep -Eq '^  migrate-sql:' "$BUILD_WORKFLOW"; then
  echo "[compute-offload] SQL must reuse build-api, not introduce another compilation job" >&2
  exit 1
fi
if grep -Eq 'dotnet[[:space:]]+(build|restore|publish|tool)|setup-dotnet|migrations script' "$DEPLOY_WORKFLOW"; then
  echo "[compute-offload] production-deploy must consume SQL, never install SDKs or compile/generate it again" >&2
  exit 1
fi
require_match '^  deploy:' "$DEPLOY_WORKFLOW" \
  "deploy-production workflow must run the rollout in a job named deploy."
require_match 'needs: \[resolve, apply-migrations\]' "$DEPLOY_WORKFLOW" \
  "deploy must wait for the Actions migration gate."
require_match 'ALLOW_VPS_SOURCE_BUILD=owner-approved-emergency' "$REPO_ROOT/scripts/deploy-production.sh" \
  "the legacy source-build fallback must be explicitly gated."
require_match 'ALLOW_VPS_SOURCE_BUILD=owner-approved-emergency' "$REPO_ROOT/scripts/deploy/deploy-direct.sh" \
  "the legacy direct source-build fallback must be explicitly gated."

# The VPS must never receive a source sync - only the ROLLOUT workflow is
# checked here. build-images.yml runs on a CI checkout, where fetching the
# diff base for the syntax gate is normal runner work and never touches the VPS.
if grep -Eq 'git[[:space:]]+(fetch|reset[[:space:]]+--hard)' "$DEPLOY_WORKFLOW"; then
  echo "[compute-offload] $DEPLOY_WORKFLOW must not sync the full source repository to the VPS" >&2
  exit 1
fi

# Two source-build patterns on purpose; neither is a superset of the other.
# The first (whitespace-anchored) also blocks lint; the second (\b-anchored)
# also catches $(npm run build), "npm test", npm run build:web and
# /usr/bin/dotnet build. verify-image-only-rollout.sh repeats the second.
active_commands="$(sed '/^[[:space:]]*#/d' "$ROLLOUT")"
if printf '%s\n' "$active_commands" | grep -Eiq \
  'docker[[:space:]]+(compose|[-a-z]+)[^\n]*([[:space:]])build([[:space:]]|$)|(^|[[:space:]])(pnpm|npm)[[:space:]]+[^\n]*(build|test|lint)([[:space:]]|$)|(^|[[:space:]])dotnet[[:space:]]+(build|test|publish)([[:space:]]|$)' \
  || printf '%s\n' "$active_commands" | grep -Eiq \
  'docker[[:space:]]+(compose|[-a-z]+)[^\n]*([[:space:]])build([[:space:]]|$)|\b(pnpm|npm)[[:space:]]+[^\n]*\b(build|test)\b|\bdotnet[[:space:]]+(build|test|publish)\b'; then
  echo "[compute-offload] active VPS rollout contains a source build/test/publish command" >&2
  exit 1
fi

# Owner Agent Console (owner directive 2026-09-27): .github/workflows/agent-console.yml
# builds on Actions and its SSH rollout (the block between the BEGIN/END
# REMOTE AGENT-CONSOLE ROLLOUT markers) must stay pull-only as well.
AGENT_CONSOLE_WORKFLOW="$REPO_ROOT/.github/workflows/agent-console.yml"
AGENT_CONSOLE_COMPOSE="$REPO_ROOT/docker-compose.agent-console.yml"
agent_console_fail() {
  echo "[compute-offload] agent-console: $1" >&2
  exit 1
}
[ -f "$AGENT_CONSOLE_WORKFLOW" ] || agent_console_fail "missing .github/workflows/agent-console.yml"
[ -f "$AGENT_CONSOLE_COMPOSE" ] || agent_console_fail "missing docker-compose.agent-console.yml"
agent_console_remote="$(awk '
  /# BEGIN REMOTE AGENT-CONSOLE ROLLOUT/ { inside = 1; next }
  /# END REMOTE AGENT-CONSOLE ROLLOUT/ { inside = 0 }
  inside
' "$AGENT_CONSOLE_WORKFLOW" | sed '/^[[:space:]]*#/d')"
[ -n "$agent_console_remote" ] || agent_console_fail "rollout script markers not found in agent-console.yml"
printf '%s\n' "$agent_console_remote" | grep -Eq 'compose[^#]*[[:space:]]pull([[:space:]]|$)' \
  || agent_console_fail "the VPS rollout must pull prebuilt GHCR images (compose pull)"
printf '%s\n' "$agent_console_remote" | grep -Eq '(^|[[:space:]])up[[:space:]][^#]*--no-build' \
  || agent_console_fail "the VPS rollout must start containers with up --no-build"
if printf '%s\n' "$agent_console_remote" | grep -E '(^|[[:space:]])up[[:space:]]+-' | grep -v -- '--no-build' | grep -q .; then
  agent_console_fail "every compose up in the VPS rollout must pass --no-build"
fi
if printf '%s\n' "$agent_console_remote" | grep -Eiq \
  'docker[[:space:]]+(build|buildx|builder)([[:space:]]|$)|docker[[:space:]]+image[[:space:]]+build|compose[^#]*[[:space:]]build([[:space:]]|$)|(^|[[:space:];|&(])(npm|npx|pnpm|yarn|node|dotnet|tsc|make)([[:space:]]|$)|git[[:space:]]+(clone|fetch|pull|checkout|reset|submodule)([[:space:]]|$)'; then
  agent_console_fail "the VPS rollout contains a build/test/install or source-sync command"
fi
if grep -Eq '^[[:space:]]+build:' "$AGENT_CONSOLE_COMPOSE"; then
  agent_console_fail "docker-compose.agent-console.yml must not declare build: sections (images come from GHCR)"
fi

echo "[compute-offload] Actions owns build, test, image-packaging, and migration generation; VPS rollout is pull-only."
