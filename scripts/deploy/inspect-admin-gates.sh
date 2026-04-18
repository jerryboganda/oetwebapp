#!/usr/bin/env bash
# Inspect the admin account's auth gates
set -euo pipefail
cd /root/oetwebsite
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

echo "=== ADMIN ACCOUNT GATES ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
  'SELECT "Id",
          "Email",
          "Role",
          CASE WHEN "PasswordHash" IS NULL OR "PasswordHash" = '"'"''"'"' THEN '"'"'NO_PWD'"'"' ELSE '"'"'SET'"'"' END AS "Password",
          "EmailVerifiedAt",
          "AuthenticatorEnabledAt",
          "DeletedAt",
          "LastLoginAt"
     FROM "ApplicationUserAccounts"
    WHERE "Email" = '"'"'master.admin@oetwithdrhesham.co.uk'"'"';'

echo ""
echo "=== EXISTING PASSWORD HASH LENGTH (to confirm format) ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -c \
  'SELECT LENGTH("PasswordHash")
     FROM "ApplicationUserAccounts"
    WHERE "Email" = '"'"'master.admin@oetwithdrhesham.co.uk'"'"';'
