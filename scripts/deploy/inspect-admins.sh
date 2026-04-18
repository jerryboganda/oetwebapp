#!/usr/bin/env bash
# Inspect admin accounts in production
set -euo pipefail
cd /root/oetwebsite

# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

echo "=== ALL ADMIN ACCOUNTS ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
  'SELECT column_name FROM information_schema.columns
     WHERE table_name='"'"'ApplicationUserAccounts'"'"'
     ORDER BY ordinal_position;'

echo ""
echo "=== ADMINS ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
  'SELECT "Id", "Email", "Role", "CreatedAt"
     FROM "ApplicationUserAccounts"
    WHERE "Role" = '"'"'admin'"'"'
       OR "Email" ILIKE '"'"'%admin%'"'"'
    ORDER BY "CreatedAt";'

echo ""
echo "=== ADMIN PERMISSION GRANTS ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
  'SELECT ua."Email", apg."Permission", apg."GrantedAt"
     FROM "AdminPermissionGrants" apg
     JOIN "ApplicationUserAccounts" ua ON ua."Id" = apg."AdminUserId"
    ORDER BY ua."Email", apg."Permission";'

echo ""
echo "=== ACCOUNTS BY ROLE SUMMARY ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
  'SELECT "Role", COUNT(*) FROM "ApplicationUserAccounts" GROUP BY "Role" ORDER BY "Role";'
