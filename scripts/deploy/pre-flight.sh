#!/usr/bin/env bash
# Production pre-flight snapshot — run on the VPS
set -euo pipefail

APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
cd "$APP_DIR"
TS=$(date -u +%Y%m%d-%H%M%S)
echo "=== BACKUP_TIMESTAMP: ${TS} ==="

bash scripts/deploy/validate-production-env.sh .env.production

read_env_value() {
  local key="$1"
  local line value
  line=$(grep -E "^${key}=" .env.production | tail -n 1)
  value="${line#*=}"
  value="${value%\"}"
  value="${value#\"}"
  value="${value%\'}"
  value="${value#\'}"
  printf '%s' "$value"
}

POSTGRES_USER=$(read_env_value POSTGRES_USER)
POSTGRES_DB=$(read_env_value POSTGRES_DB)
export POSTGRES_USER POSTGRES_DB

echo "--- Running encrypted backup sidecar snapshot..."
docker compose --env-file .env.production -f docker-compose.production.yml run --rm -e RUN_ONCE_NOW=YES db-backup

echo "--- Migration count BEFORE ---"
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At \
  -c 'SELECT COUNT(*) FROM "__EFMigrationsHistory";'

echo "--- Last 5 migrations applied ---"
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At \
  -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 5;'

echo "--- Image inventory (for rollback) ---"
docker images --format 'table {{.Repository}}\t{{.Tag}}\t{{.ID}}\t{{.CreatedSince}}' \
  | grep -E 'oetwebsite|postgres' | head -10

echo "--- Current HEAD ---"
git rev-parse HEAD
git log --oneline -1

echo "=== PRE_FLIGHT_DONE ==="
