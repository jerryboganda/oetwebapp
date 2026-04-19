#!/usr/bin/env bash
# Check which of the three permission-related tables exist in prod.
set -euo pipefail
cd /root/oetwebsite
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

echo "=== Which of these tables exist? ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT tablename FROM pg_tables
 WHERE schemaname='public'
   AND tablename IN ('AdminPermissionGrants', 'AdminUsers', 'PermissionTemplates')
 ORDER BY tablename;"
