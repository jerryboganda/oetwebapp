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

detect_destructive_migration() {
  local previous_sha="$1"
  local target_sha="$2"
  if [ -z "$previous_sha" ] || ! git cat-file -e "$previous_sha^{commit}" 2>/dev/null; then
    echo "--- Destructive migration check skipped: no previous-good SHA is recorded yet ---"
    return 0
  fi
  if [ "$previous_sha" = "$target_sha" ]; then
    echo "--- Destructive migration check: unchanged SHA ---"
    return 0
  fi
  changed_migrations=$(git diff --name-only "$previous_sha" "$target_sha" -- backend/src/OetLearner.Api/Data/Migrations || true)
  if [ -z "$changed_migrations" ]; then
    echo "--- Destructive migration check: no migration files changed ---"
    return 0
  fi
  if git diff "$previous_sha" "$target_sha" -- backend/src/OetLearner.Api/Data/Migrations \
      | grep -Ei 'Drop(Column|Table|Index|ForeignKey|PrimaryKey)|migrationBuilder\.Sql\("[[:space:]]*(DROP|TRUNCATE|DELETE)[[:space:]]' >/dev/null; then
    return 1
  fi
  echo "--- Destructive migration check: migration files changed, no destructive pattern detected ---"
  return 0
}

require_destructive_migration_approval() {
  local missing=0
  if [ "${DESTRUCTIVE_MIGRATION_APPROVAL:-}" != "approved-by-dr-faisal-maqsood" ]; then
    echo "[migration] DESTRUCTIVE_MIGRATION_APPROVAL=approved-by-dr-faisal-maqsood is required." >&2
    missing=1
  fi
  for key in DESTRUCTIVE_MIGRATION_MAINTENANCE_WINDOW DESTRUCTIVE_MIGRATION_BACKUP_ID DESTRUCTIVE_MIGRATION_RESTORE_DRILL_ID; do
    if [ -z "${!key:-}" ]; then
      echo "[migration] $key is required for destructive migrations." >&2
      missing=1
    fi
  done
  if [ "$missing" -ne 0 ]; then
    exit 1
  fi
}

TARGET_SHA=$(git rev-parse HEAD)
PREVIOUS_GOOD_SHA=""
if [ -s .deploy/previous-good.env ]; then
  PREVIOUS_GOOD_SHA=$(awk -F= '$1 == "PREVIOUS_GOOD_SHA" { print $2 }' .deploy/previous-good.env | tail -n 1)
fi

if ! detect_destructive_migration "$PREVIOUS_GOOD_SHA" "$TARGET_SHA"; then
  echo "--- Destructive migration risk detected between ${PREVIOUS_GOOD_SHA:-unknown} and $TARGET_SHA ---" >&2
  require_destructive_migration_approval
  echo "--- Destructive migration approval package accepted ---"
fi

echo "=== PRE_FLIGHT_DONE ==="
