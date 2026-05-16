#!/usr/bin/env bash
# Production deploy — run on the VPS
# Matches AGENTS.md deploy runbook.
set -euo pipefail

APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
APP_PUBLIC_URL="${APP_PUBLIC_URL:-https://app.oetwithdrhesham.co.uk}"
API_PUBLIC_URL="${API_PUBLIC_URL:-https://api.oetwithdrhesham.co.uk}"
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
EVIDENCE_DIR="${EVIDENCE_DIR:-release-evidence-$DEPLOY_REF}"
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

echo "--- git fetch origin main/tags ---"
git fetch origin main --tags
if ! git cat-file -e "$DEPLOY_REF^{commit}" 2>/dev/null; then
	echo "--- git fetch exact deploy SHA ---"
	git fetch origin "$DEPLOY_REF"
fi

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
git clean -fd -e "$EVIDENCE_DIR/" -e .deploy/

echo "--- Running pre-flight snapshot ---"
VPS_APP_DIR="$APP_DIR" DEPLOY_REF="$DEPLOY_SHA" bash ./scripts/deploy/pre-flight.sh

echo "--- Running immutable-image rollout driver ---"
VPS_APP_DIR="$APP_DIR" EVIDENCE_DIR="$EVIDENCE_DIR" APP_PUBLIC_URL="$APP_PUBLIC_URL" API_PUBLIC_URL="$API_PUBLIC_URL" bash ./scripts/deploy/rollout-release.sh

echo "=== DEPLOY_DONE: $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
