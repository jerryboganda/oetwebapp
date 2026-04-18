#!/usr/bin/env bash
# Two actions in one transaction:
#  1. Create admin on manwara575@gmail.com (no usable password — reset via OTP)
#  2. Demote mindreader420123@gmail.com from admin → learner so you can use
#     it as a personal learner account without any admin privileges leaking.
# Old master.admin@oetwithdrhesham.co.uk stays untouched as safety net.
set -euo pipefail
cd /root/oetwebsite
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

NEW_EMAIL="manwara575@gmail.com"
NEW_EMAIL_NORMALIZED="$(echo "$NEW_EMAIL" | tr '[:upper:]' '[:lower:]')"
NEW_ID="auth_admin_$(head -c 16 /dev/urandom | od -An -tx1 | tr -d ' \n')"
NOW_UTC="$(date -u +'%Y-%m-%d %H:%M:%S.%6N+00')"
SENTINEL_HASH='!_DISABLED_PENDING_RESET_'"$(head -c 32 /dev/urandom | base64 | tr -d '\n=')"

echo "=== ACTIONS ==="
echo "1. Create admin: ${NEW_EMAIL}  (Id: ${NEW_ID})"
echo "2. Demote mindreader420123@gmail.com  admin -> learner"
echo ""

docker exec -i oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" <<SQL
BEGIN;

-- 1. Create new admin (guarded against duplicate by normalised email).
INSERT INTO "ApplicationUserAccounts"
    ("Id", "Email", "NormalizedEmail", "PasswordHash", "Role",
     "EmailVerifiedAt", "CreatedAt", "UpdatedAt")
SELECT
    '$NEW_ID',
    '$NEW_EMAIL',
    '$NEW_EMAIL_NORMALIZED',
    '$SENTINEL_HASH',
    'admin',
    '$NOW_UTC',
    '$NOW_UTC',
    '$NOW_UTC'
WHERE NOT EXISTS (
    SELECT 1 FROM "ApplicationUserAccounts"
     WHERE "NormalizedEmail" = '$NEW_EMAIL_NORMALIZED'
);

-- 2. Demote mindreader420123 admin -> learner. Also revoke any live
--    refresh tokens so any active admin session it might have becomes
--    invalid immediately.
UPDATE "ApplicationUserAccounts"
   SET "Role" = 'learner',
       "UpdatedAt" = '$NOW_UTC'
 WHERE "NormalizedEmail" = 'mindreader420123@gmail.com'
   AND "Role" = 'admin';

UPDATE "RefreshTokenRecords"
   SET "RevokedAt" = '$NOW_UTC'
 WHERE "ApplicationUserAccountId" = (
         SELECT "Id" FROM "ApplicationUserAccounts"
          WHERE "NormalizedEmail" = 'mindreader420123@gmail.com')
   AND "RevokedAt" IS NULL;

-- 3. Audit trail.
INSERT INTO "AuditEvents"
    ("Id", "OccurredAt", "ActorId", "ActorName",
     "Action", "ResourceType", "ResourceId", "Details")
VALUES
    (replace(gen_random_uuid()::text, '-', ''),
     NOW(), 'root@vps-manual', 'Manual recovery script',
     'AdminAccountCreated', 'ApplicationUserAccount', '$NEW_ID',
     'Created admin on ${NEW_EMAIL} via DB recovery path. Password pending via /v1/auth/forgot-password.'),
    (replace(gen_random_uuid()::text, '-', ''),
     NOW(), 'root@vps-manual', 'Manual recovery script',
     'AdminAccountDemoted', 'ApplicationUserAccount',
     (SELECT "Id" FROM "ApplicationUserAccounts" WHERE "NormalizedEmail" = 'mindreader420123@gmail.com'),
     'Demoted mindreader420123@gmail.com admin -> learner. Refresh tokens revoked.');

COMMIT;

\echo ''
\echo '=== FINAL ADMIN ROSTER ==='
SELECT "Id", "Email", "Role", "CreatedAt", "LastLoginAt"
  FROM "ApplicationUserAccounts"
 WHERE "Role" = 'admin'
 ORDER BY "CreatedAt";

\echo ''
\echo '=== mindreader420123 new state ==='
SELECT "Id", "Email", "Role", "UpdatedAt"
  FROM "ApplicationUserAccounts"
 WHERE "NormalizedEmail" = 'mindreader420123@gmail.com';
SQL
