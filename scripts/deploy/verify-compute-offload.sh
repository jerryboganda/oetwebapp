#!/usr/bin/env bash
# Static deployment contract for the production compute-offload path:
# GitHub Actions builds, tests and generates migrations; the VPS only pulls
# pre-built images and runs data/runtime gates.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
WORKFLOW="$REPO_ROOT/.github/workflows/deploy.yml"
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

require_literal "$WORKFLOW" 'migrations script --idempotent'
require_literal "$WORKFLOW" 'apply-migrations-from-ci.sh'
require_literal "$ROLLOUT" '--no-build'
require_literal "$ROLLOUT" 'DB_BACKUP_IMAGE'
require_literal "$IMMUTABLE_ROLLOUT" '--no-build'
require_literal "$PREFLIGHT" '--no-build'
# migrate-production must wait for all pre-built images.
require_literal "$WORKFLOW" 'needs: [build-web, build-api, build-backup]'

require_match '^  migrate-production:' "$WORKFLOW" \
  "deploy workflow must generate/apply migrations in the migrate-production Actions job."
require_match 'needs: \[build-web, build-api, build-backup, build-agent-gateway, migrate-production\]' "$WORKFLOW" \
  "deploy must wait for the Actions migration gate."
require_match 'ALLOW_VPS_SOURCE_BUILD=owner-approved-emergency' "$REPO_ROOT/scripts/deploy-production.sh" \
  "the legacy source-build fallback must be explicitly gated."
require_match 'ALLOW_VPS_SOURCE_BUILD=owner-approved-emergency' "$REPO_ROOT/scripts/deploy/deploy-direct.sh" \
  "the legacy direct source-build fallback must be explicitly gated."

if grep -Eq 'git[[:space:]]+(fetch|reset[[:space:]]+--hard)' "$WORKFLOW"; then
  echo "[compute-offload] deploy workflow must not sync the full source repository to the VPS" >&2
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
