#!/usr/bin/env bash
# Inspect the admin account's auth gates
set -euo pipefail
APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
cd "$APP_DIR"
: "${ADMIN_EMAIL:?Set ADMIN_EMAIL to inspect one admin account}"
if ! printf '%s' "$ADMIN_EMAIL" | grep -Eq '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'; then
  echo "ADMIN_EMAIL must be a simple email address" >&2
  exit 1
fi
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

echo "=== ADMIN ACCOUNT GATES ==="
docker exec -i oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v admin_email="$ADMIN_EMAIL" <<'SQL'
SELECT "Id",
          "Email",
          "Role",
          CASE WHEN "PasswordHash" IS NULL OR "PasswordHash" = '' THEN 'NO_PWD' ELSE 'SET' END AS "Password",
          "EmailVerifiedAt",
          "AuthenticatorEnabledAt",
          "DeletedAt",
          "LastLoginAt"
     FROM "ApplicationUserAccounts"
    WHERE "Email" = :'admin_email';
SQL

echo ""
echo "=== EXISTING PASSWORD HASH LENGTH (to confirm format) ==="
docker exec -i oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -v admin_email="$ADMIN_EMAIL" <<'SQL'
SELECT LENGTH("PasswordHash")
     FROM "ApplicationUserAccounts"
    WHERE "Email" = :'admin_email';
SQL
