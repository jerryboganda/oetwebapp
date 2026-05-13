#!/usr/bin/env bash
# Create a new admin account on production, with no usable password.
# The operator recovers access via /v1/auth/forgot-password which sends
# an OTP to the new email. Once the OTP + new password are set via the
# UI, this account becomes fully usable.
#
# Every step is idempotent-ish: re-running if the account already exists
# will no-op (via ON CONFLICT DO NOTHING pattern simulated by WHERE-NOT-EXISTS).
set -euo pipefail
APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
cd "$APP_DIR"

# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

: "${NEW_EMAIL:?Set NEW_EMAIL to the admin recovery email before running}"
OPERATOR_CONTEXT="${OPERATOR_CONTEXT:-manual recovery approved by deploy owner}"
NEW_EMAIL_NORMALIZED="$(echo "$NEW_EMAIL" | tr '[:upper:]' '[:lower:]')"
if ! printf '%s' "$NEW_EMAIL_NORMALIZED" | grep -Eq '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'; then
    echo "NEW_EMAIL must be a simple email address" >&2
    exit 1
fi
if [ "${#NEW_EMAIL_NORMALIZED}" -gt 320 ]; then
    echo "NEW_EMAIL is too long" >&2
    exit 1
fi
if [ "${#OPERATOR_CONTEXT}" -gt 500 ]; then
    echo "OPERATOR_CONTEXT is too long" >&2
    exit 1
fi
NEW_ID="auth_admin_$(head -c 16 /dev/urandom | od -An -tx1 | tr -d ' \n')"
NOW_UTC="$(date -u +'%Y-%m-%d %H:%M:%S.%6N+00')"

echo "=== CREATING NEW ADMIN ==="
echo "Id:              $NEW_ID"
echo "Email:           $NEW_EMAIL"
echo "NormalizedEmail: $NEW_EMAIL_NORMALIZED"
echo ""

# The PasswordHash is a sentinel that CANNOT validate any password —
# it's not a real ASP.NET Identity v3 hash. Password login therefore fails
# closed until /v1/auth/reset-password writes a real hash.
# We still have to put something non-NULL because the column is NOT NULL.
SENTINEL_HASH='!_DISABLED_PENDING_RESET_'"$(head -c 32 /dev/urandom | base64 | tr -d '\n=')"

docker exec -i oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
    -v new_id="$NEW_ID" \
    -v new_email="$NEW_EMAIL" \
    -v new_email_normalized="$NEW_EMAIL_NORMALIZED" \
    -v sentinel_hash="$SENTINEL_HASH" \
    -v now_utc="$NOW_UTC" \
    -v operator_context="$OPERATOR_CONTEXT" <<'SQL'
BEGIN;

-- 1. Create the admin account only if one doesn't already exist on this email.
INSERT INTO "ApplicationUserAccounts"
    ("Id", "Email", "NormalizedEmail", "PasswordHash", "Role",
     "EmailVerifiedAt", "CreatedAt", "UpdatedAt")
SELECT
    :'new_id',
    :'new_email',
    :'new_email_normalized',
    :'sentinel_hash',
    'admin',
    :'now_utc',
    :'now_utc',
    :'now_utc'
WHERE NOT EXISTS (
    SELECT 1 FROM "ApplicationUserAccounts"
     WHERE "NormalizedEmail" = :'new_email_normalized'
);

-- 2. Audit-trail the creation.
INSERT INTO "AuditEvents"
    ("Id", "OccurredAt", "ActorId", "ActorName",
     "Action", "ResourceType", "ResourceId", "Details")
VALUES
    (replace(gen_random_uuid()::text, '-', ''),
     NOW(),
     'root@vps-manual',
     'Manual recovery script',
     'AdminAccountCreated',
     'ApplicationUserAccount',
    :'new_id',
    'Created via DB recovery path. Password pending via /v1/auth/forgot-password OTP flow. Context: ' || :'operator_context');

COMMIT;

-- 3. Verify the new account exists + report state.
\echo ''
\echo '=== NEW ADMIN STATE ==='
SELECT "Id", "Email", "Role", "EmailVerifiedAt", "AuthenticatorEnabledAt", "CreatedAt"
  FROM "ApplicationUserAccounts"
 WHERE "NormalizedEmail" = :'new_email_normalized';

\echo ''
\echo '=== ALL ADMIN ACCOUNTS (old + new side-by-side) ==='
SELECT "Id", "Email", "Role", "CreatedAt", "LastLoginAt"
  FROM "ApplicationUserAccounts"
 WHERE "Role" = 'admin'
 ORDER BY "CreatedAt";
SQL
