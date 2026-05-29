#!/usr/bin/env bash
# Deprecated compatibility entrypoint. Production deploys must pass through the
# exact-SHA plus immutable image digest gate in scripts/deploy/deploy-prod.sh.
set -euo pipefail

APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
cd "$APP_DIR"

echo "scripts/vps-deploy.sh is deprecated; delegating to scripts/deploy/deploy-prod.sh." >&2
echo "Set DEPLOY_REF=<sha> plus WEB_IMAGE/API_IMAGE/DB_BACKUP_IMAGE/ROUTER_IMAGE digest refs for rollback deployments." >&2
exec bash ./scripts/deploy/deploy-prod.sh "$@"
