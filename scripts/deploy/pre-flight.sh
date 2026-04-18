#!/usr/bin/env bash
# Production pre-flight snapshot — run on the VPS
set -euo pipefail

cd /root/oetwebsite
TS=$(date -u +%Y%m%d-%H%M%S)
echo "=== BACKUP_TIMESTAMP: ${TS} ==="

mkdir -p /root/oet-backups

# Pull Postgres creds from env file
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

echo "--- Dumping postgres (compressed custom format)..."
docker exec oet-postgres pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --no-owner --no-acl \
  > "/root/oet-backups/prod-dump-${TS}.dump"
ls -lh "/root/oet-backups/prod-dump-${TS}.dump"

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
