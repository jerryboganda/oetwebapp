#!/usr/bin/env bash
# Production deploy — run on the VPS
# Matches AGENTS.md deploy runbook.
set -euo pipefail

APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
EVIDENCE_DIR="${EVIDENCE_DIR:-release-evidence}"
APP_PUBLIC_URL="${APP_PUBLIC_URL:-https://app.oetwithdrhesham.co.uk}"
API_PUBLIC_URL="${API_PUBLIC_URL:-https://api.oetwithdrhesham.co.uk}"
DEPLOY_REF="${DEPLOY_REF:-origin/main}"
cd "$APP_DIR"
if [ "$(pwd)" != "/opt/oetwebapp" ]; then
	echo "Refusing production deploy from stale/noncanonical checkout: $(pwd)" >&2
	exit 1
fi

read_env_value() {
	local key="$1"
	local line value
	line=$(grep -E "^${key}=" .env.production | tail -n 1 || true)
	if [ -z "$line" ]; then
		return 1
	fi
	value="${line#*=}"
	value="${value%\"}"
	value="${value#\"}"
	value="${value%\'}"
	value="${value#\'}"
	printf '%s' "$value"
}

echo "=== DEPLOY_START: $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="

echo "--- git fetch origin main ---"
git fetch origin main --tags

DEPLOY_SHA="$(git rev-parse --verify "$DEPLOY_REF^{commit}")"
echo "Target deploy ref: $DEPLOY_REF -> $DEPLOY_SHA"
git log --oneline -1 "$DEPLOY_SHA"

echo "--- Verifying signed release evidence for target HEAD before target scripts run ---"
ENV_EVIDENCE_SIGNER_FINGERPRINT="$(read_env_value EVIDENCE_SIGNER_FINGERPRINT)"
EXPECTED_EVIDENCE_SIGNER_FINGERPRINT="${EVIDENCE_SIGNER_FINGERPRINT:-$ENV_EVIDENCE_SIGNER_FINGERPRINT}"
if [ "$ENV_EVIDENCE_SIGNER_FINGERPRINT" != "$EXPECTED_EVIDENCE_SIGNER_FINGERPRINT" ]; then
	echo "EVIDENCE_SIGNER_FINGERPRINT does not match .env.production; refusing deploy." >&2
	exit 1
fi
EVIDENCE_DIR="$EVIDENCE_DIR" EVIDENCE_ENV=production EVIDENCE_SIGNER_FINGERPRINT="$EXPECTED_EVIDENCE_SIGNER_FINGERPRINT" EXPECTED_GIT_SHA="$DEPLOY_SHA" bash ./scripts/evidence-verify.sh

echo "--- git reset --hard $DEPLOY_SHA ---"
git reset --hard "$DEPLOY_SHA"
echo "HEAD now at: $DEPLOY_SHA"
git log --oneline -1

echo "--- Preserving evidence bundle while cleaning untracked files ---"
git clean -fd -e "$EVIDENCE_DIR/"

echo "--- Running pre-flight snapshot ---"
VPS_APP_DIR="$APP_DIR" bash ./scripts/deploy/pre-flight.sh

echo "--- Running production deploy driver (sequential build + no volume destruction) ---"
bash ./scripts/deploy-production.sh

echo "--- Running post-deploy verification ---"
APP_PUBLIC_URL="$APP_PUBLIC_URL" API_PUBLIC_URL="$API_PUBLIC_URL" bash ./scripts/deploy/post-deploy-verify.sh

echo "--- Running observability smoke ---"
BASE_URL="$APP_PUBLIC_URL" API_BASE_URL="$API_PUBLIC_URL" OBSERVABILITY_SMOKE_OUTPUT="/tmp/observability-smoke-production.json" bash ./scripts/observability-smoke.sh

echo "--- Running Reading/media smoke gate ---"
API_PUBLIC_URL="$API_PUBLIC_URL" bash ./scripts/deploy/reading-media-smoke.sh

echo "=== DEPLOY_DONE: $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
