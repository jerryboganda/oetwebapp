#!/usr/bin/env bash
# Deployment safety guard: the active production rollout may pull and start
# pre-built images, but it must never compile or test on the VPS.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROLLOUT_SCRIPT="$SCRIPT_DIR/auto-deploy-ghcr.sh"

if [ ! -f "$ROLLOUT_SCRIPT" ]; then
  echo "[deploy-guard] active image-only rollout script is missing: $ROLLOUT_SCRIPT" >&2
  exit 1
fi

active_commands="$(sed '/^[[:space:]]*#/d' "$ROLLOUT_SCRIPT")"
if printf '%s\n' "$active_commands" | grep -Eiq \
  'docker[[:space:]]+(compose|[-a-z]+)[^\n]*([[:space:]])build([[:space:]]|$)|\b(pnpm|npm)[[:space:]]+[^\n]*\b(build|test)\b|\bdotnet[[:space:]]+(build|test|publish)\b'; then
  echo "[deploy-guard] active VPS rollout contains a source build/test command." >&2
  exit 1
fi

if ! grep -Eq -- '--no-build' "$ROLLOUT_SCRIPT"; then
  echo "[deploy-guard] active VPS rollout must use docker compose --no-build." >&2
  exit 1
fi

echo "[deploy-guard] active production rollout is image-only; compute-heavy work stays in GitHub Actions."
