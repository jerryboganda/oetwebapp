#!/usr/bin/env bash
# Production deploy — run on the VPS
# Matches AGENTS.md deploy runbook.
set -euo pipefail

APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
APP_PUBLIC_URL="${APP_PUBLIC_URL:-https://app.oetwithdrhesham.co.uk}"
API_PUBLIC_URL="${API_PUBLIC_URL:-https://api.oetwithdrhesham.co.uk}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DRIVER_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
WEB_IMAGE="${WEB_IMAGE:?Set WEB_IMAGE to an immutable @sha256 web image ref.}"
API_IMAGE="${API_IMAGE:?Set API_IMAGE to an immutable @sha256 API image ref.}"
DB_BACKUP_IMAGE="${DB_BACKUP_IMAGE:?Set DB_BACKUP_IMAGE to an immutable @sha256 DB backup image ref.}"
ROUTER_IMAGE="${ROUTER_IMAGE:?Set ROUTER_IMAGE to an immutable @sha256 router image ref.}"
DEPLOY_REF="${DEPLOY_REF:?Set DEPLOY_REF to the exact 40-character Git SHA to deploy.}"
case "$DEPLOY_REF" in
	*[!0-9a-fA-F]*)
		echo "DEPLOY_REF must be a full 40-character hexadecimal SHA; found: $DEPLOY_REF" >&2
		exit 1
		;;
esac
if [ "${#DEPLOY_REF}" -ne 40 ]; then
	echo "DEPLOY_REF must be a full 40-character SHA; found length ${#DEPLOY_REF}" >&2
	exit 1
fi
cd "$APP_DIR"
if [ "$(pwd)" != "/opt/oetwebapp" ]; then
	echo "Refusing production deploy from stale/noncanonical checkout: $(pwd)" >&2
	exit 1
fi

echo "=== DEPLOY_START: $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="

echo "--- Preserving current deploy driver ---"
rm -rf .deploy/deploy-driver
mkdir -p .deploy/deploy-driver
cp -R "$DRIVER_ROOT/scripts" .deploy/deploy-driver/scripts

echo "--- git fetch origin main/tags ---"
git fetch origin main --tags
if ! git cat-file -e "$DEPLOY_REF^{commit}" 2>/dev/null; then
	echo "--- git fetch exact deploy SHA ---"
	git fetch origin "$DEPLOY_REF"
fi

DEPLOY_SHA="$(git rev-parse --verify "$DEPLOY_REF^{commit}")"
echo "Target deploy ref: $DEPLOY_REF -> $DEPLOY_SHA"
git log --oneline -1 "$DEPLOY_SHA"

echo "--- git reset --hard $DEPLOY_SHA ---"
git reset --hard "$DEPLOY_SHA"
echo "HEAD now at: $DEPLOY_SHA"
git log --oneline -1

echo "--- Cleaning untracked files while preserving deploy state ---"
git clean -fd -e .deploy/

echo "--- Running pre-flight snapshot ---"
VPS_APP_DIR="$APP_DIR" DEPLOY_REF="$DEPLOY_SHA" bash .deploy/deploy-driver/scripts/deploy/pre-flight.sh

echo "--- Running immutable-image rollout driver ---"
VPS_APP_DIR="$APP_DIR" WEB_IMAGE="$WEB_IMAGE" API_IMAGE="$API_IMAGE" DB_BACKUP_IMAGE="$DB_BACKUP_IMAGE" ROUTER_IMAGE="$ROUTER_IMAGE" APP_PUBLIC_URL="$APP_PUBLIC_URL" API_PUBLIC_URL="$API_PUBLIC_URL" bash .deploy/deploy-driver/scripts/deploy/rollout-release.sh

echo "=== DEPLOY_DONE: $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
