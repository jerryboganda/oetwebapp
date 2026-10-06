#!/usr/bin/env bash
# Static contract of the fleet pipeline (owner directive 2026-10-05). Reads files only. Runs in the always-executing `guards`
# job of .github/workflows/fleet.yml. It is deliberately NOT under scripts/deploy/ (the production pipeline contract is a
# separate, non-bypassable tool); it mirrors the pull-only rules of verify-compute-offload.sh for the fleet's own rollout.
#
# Enforced:
#   1. the SSH rollout block between "# BEGIN REMOTE FLEET ROLLOUT" and "# END REMOTE FLEET ROLLOUT" is pull-only:
#      compose pull, every compose up passes --no-build, no docker build, no npm/pnpm/yarn/node/dotnet/tsc/make, no source sync,
#      no mutable :latest tag;
#   2. fleet.yml never trusts a host key on first use, always pins the host key with StrictHostKeyChecking=yes, never mentions the production
#      rollout script, never runs Playwright, has no pull_request/schedule trigger, keeps the shared-source path filter, never runs
#      a built image (BUILD-ONLY: no smoke or self-check job; owner directive 2026-10-06), and only its sync job holds the VPS
#      credentials, with a read-only packages token;
#   3. no secret-shaped string exists anywhere under platform/fleet or in fleet.yml.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
WORKFLOW="${FLEET_WORKFLOW_FILE:-$REPO_ROOT/.github/workflows/fleet.yml}"
SCAN_ROOT="${FLEET_SCAN_ROOT:-$REPO_ROOT/platform/fleet}"

fail() {
  echo "[fleet-contract] $1" >&2
  exit 1
}

[ -f "$WORKFLOW" ] || fail "missing $WORKFLOW"

active="$(sed '/^[[:space:]]*#/d' "$WORKFLOW")"

# ---- 1. the rollout block ----------------------------------------------------------------------------------------
remote="$(awk '
  /# BEGIN REMOTE FLEET ROLLOUT/ { inside = 1; next }
  /# END REMOTE FLEET ROLLOUT/ { inside = 0 }
  inside
' "$WORKFLOW" | sed '/^[[:space:]]*#/d')"
[ -n "$remote" ] || fail "rollout script markers (BEGIN/END REMOTE FLEET ROLLOUT) not found in fleet.yml"
grep -c 'BEGIN REMOTE FLEET ROLLOUT' "$WORKFLOW" | grep -qx 1 || fail "exactly one BEGIN marker is allowed"
grep -c 'END REMOTE FLEET ROLLOUT' "$WORKFLOW" | grep -qx 1 || fail "exactly one END marker is allowed"

printf '%s\n' "$remote" | grep -Eq 'compose[^#]*[[:space:]]pull([[:space:]]|$)' \
  || fail "the VPS rollout must pull prebuilt GHCR images (compose pull)"
printf '%s\n' "$remote" | grep -Eq '(^|[[:space:]])up[[:space:]][^#]*--no-build' \
  || fail "the VPS rollout must start containers with up --no-build"
if printf '%s\n' "$remote" | grep -E '(^|[[:space:]])up[[:space:]]+-' | grep -v -- '--no-build' | grep -q .; then
  fail "every compose up in the VPS rollout must pass --no-build"
fi
if printf '%s\n' "$remote" | grep -Eiq \
  'docker[[:space:]]+(build|buildx|builder)([[:space:];|&)]|$)|docker[[:space:]]+image[[:space:]]+build|compose[^#]*[[:space:]]build([[:space:];|&)]|$)|(^|[[:space:];|&(])(npm|npx|pnpm|yarn|node|dotnet|tsc|make)([[:space:];|&)]|$)|git[[:space:]]+(clone|fetch|pull|checkout|reset|submodule)([[:space:];|&)]|$)'; then
  fail "the VPS rollout contains a build/test/install or source-sync command"
fi
if printf '%s\n' "$remote" | grep -Eq ':latest([^A-Za-z0-9_.-]|$)|@latest'; then
  fail "the VPS rollout must never reference a mutable :latest tag (images are pulled by digest)"
fi
printf '%s\n' "$remote" | grep -q 'docker logout' || fail "the registry credential must be removed again (docker logout in a trap)"
printf '%s\n' "$remote" | grep -q -- '--password-stdin' || fail "the registry token must travel on stdin (--password-stdin)"

# ---- 2. the workflow as a whole -------------------------------------------------------------------------------
# Spelled in two pieces so this file never contains the trust-on-first-use value itself (the repository tests scan every shipped file for it).
first_use="accept""-new"
if printf '%s\n' "$active" | grep -Eq "$first_use"; then
  fail "fleet.yml must never trust a host key on first use (pin the host key)"
fi
printf '%s\n' "$active" | grep -Eq 'StrictHostKeyChecking=yes' || fail "fleet.yml must connect with StrictHostKeyChecking=yes"
printf '%s\n' "$active" | grep -Eq 'UserKnownHostsFile' || fail "fleet.yml must use a pinned known_hosts file"
if printf '%s\n' "$active" | grep -q 'auto-deploy-ghcr.sh'; then
  fail "fleet.yml must not reference the production rollout script (only production-deploy.yml may)"
fi
if printf '%s\n' "$active" | grep -Eq 'playwright|merge-reports'; then
  fail "fleet.yml must not run Playwright"
fi
if printf '%s\n' "$active" | grep -Eq '(:|@)latest'; then
  fail "fleet.yml must not publish or consume a mutable latest tag"
fi

on_block="$(awk '/^on:/ { inside = 1; print; next } inside && /^[^[:space:]#]/ { inside = 0 } inside' "$WORKFLOW")"
printf '%s\n' "$on_block" | grep -Eq '^[[:space:]]+workflow_dispatch:' || fail "fleet.yml must offer workflow_dispatch"
if printf '%s\n' "$on_block" | grep -Eq '^[[:space:]]+(pull_request|pull_request_target|schedule):'; then
  fail "fleet.yml must not run on pull_request or a schedule"
fi
for required in "'platform/fleet/**'" \
  "'backend/src/OetLearner.Api/Services/Content/PdfPigPdfTextExtractor.cs'" \
  "'backend/src/OetLearner.Api/Services/Content/PdfExtractionFacts.cs'" \
  "'backend/src/OetLearner.Api/Services/Content/PdfTextEngine.cs'" \
  "'backend/src/OetLearner.Api/Services/Speaking/PcmJoiner.cs'" \
  "'backend/src/OetLearner.Api/Services/Companion/CompanionChunker.cs'" \
  "'backend/src/OetLearner.Api/Services/Companion/CompanionIndexWriter.cs'" \
  "'backend/src/OetLearner.Api/Services/Content/ContentTextExtractionService.cs'" \
  "'backend/src/OetLearner.Api/OetLearner.Api.csproj'" \
  "'global.json'" \
  "'backend/Directory.Build.props'"; do
  printf '%s\n' "$on_block" | grep -qF -- "$required" || fail "fleet.yml push paths must include $required"
done

# The only job that may touch the VPS is the opt-in sync job.
if printf '%s\n' "$active" | grep -E '^[[:space:]]+(ssh|scp|rsync)[[:space:]]' | grep -v -e 'ssh "\${ssh_opts' -e 'ssh-keyscan' | grep -q .; then
  fail "fleet.yml may only reach the VPS through the checked ssh_opts array in the sync job"
fi
sync_block="$(awk '/^  sync:/ { inside = 1; print; next } inside && /^  [a-z][a-z0-9-]*:/ { inside = 0 } inside' "$WORKFLOW")"
[ -n "$sync_block" ] || fail "the sync job is missing"
printf '%s\n' "$sync_block" | grep -q "workflow_dispatch" || fail "the sync job must be reachable only by workflow_dispatch"
printf '%s\n' "$sync_block" | grep -q 'inputs.sync' || fail "the sync job must require the sync input"
if awk '/^  sync:/ { inside = 1 } !inside { print }' "$WORKFLOW" | sed '/^[[:space:]]*#/d' | grep -Eq 'PROD_SSH_KEY|PROD_SSH_KNOWN_HOSTS|VPS_HOST'; then
  fail "only the sync job may use the production SSH credentials"
fi
# The registry token the sync job hands to the manager is its own GITHUB_TOKEN: it must be read-only.
printf '%s\n' "$sync_block" | grep -Eq '^[[:space:]]+packages:[[:space:]]+read[[:space:]]*$' || fail "the sync job must scope its token to packages: read"
if printf '%s\n' "$sync_block" | grep -Eq 'packages:[[:space:]]+write'; then
  fail "the sync job must not hold packages: write (its token is handed to the manager)"
fi
# BUILD-ONLY (owner directive 2026-10-06): images are compiled and pushed, never executed on the runner (no smoke or self-check).
if printf '%s\n' "$active" | grep -Eq '(^|[[:space:];|&(])docker[[:space:]]+(run|compose[[:space:]]+run)([[:space:]]|$)'; then
  fail "fleet.yml is BUILD-ONLY: it must not run a built image (no smoke, self-check or test job)"
fi

# ---- 3. secret-shaped strings ---------------------------------------------------------------------------------
patterns='-----BEGIN (RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{36}|github_pat_[A-Za-z0-9_]{20,}|orw1_[0-9a-f]{16}_[A-Za-z0-9_-]{43}|ofs1_[0-9a-f]{16}_[A-Za-z0-9_-]{43}|AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9]{32,}'
hits="$(grep -rEn --exclude-dir=bin --exclude-dir=obj --exclude-dir=publish --exclude-dir=node_modules --exclude-dir=TestResults "$patterns" "$SCAN_ROOT" "$WORKFLOW" 2>/dev/null || true)"
if [ -n "$hits" ]; then
  printf '%s\n' "$hits" | cut -d: -f1,2 >&2
  fail "secret-shaped strings found at the file:line positions above (values are never printed); keep every credential out of the repository"
fi

echo "[fleet-contract] OK: pull-only pinned-host-key rollout block, no mutable tags, dispatch-only VPS access, no secrets."
