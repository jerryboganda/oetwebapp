#!/usr/bin/env bash
# Triple-fix transaction:
#   1. Fix NormalizedEmail for the new admin (manwara575) -> UPPER.
#      The app's AuthEmailAddress.NormalizeOrThrow produces uppercase;
#      my earlier creation script wrote it lowercase, so all lookups
#      (sign-in, forgot-password, OTP verify) silently failed.
#   2. Delete the duplicate mindreader420123 row I created on top of the
#      real learner account (auth_aac85e6deda340059cec27c01cdc8bc6).
#      Keeps the genuine learner row intact, removes the broken clone.
#   3. Delete old master.admin@oetwithdrhesham.co.uk now that the new
#      admin can actually receive OTPs.
#
# All in one BEGIN/COMMIT. If any step fails, everything rolls back.
set -euo pipefail
cd /root/oetwebsite
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

NOW_UTC="$(date -u +'%Y-%m-%d %H:%M:%S.%6N+00')"

docker exec -i oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" <<'SQL'
BEGIN;

-- 1. Delete the broken duplicate mindreader row FIRST (before renormalising)
--    to avoid uniqueness-constraint collisions on NormalizedEmail.
DELETE FROM "RefreshTokenRecords"
 WHERE "ApplicationUserAccountId" = 'auth_admin_1f0b668cc21f40c283ffa189a1420309';
DELETE FROM "EmailOtpChallenges"
 WHERE "ApplicationUserAccountId" = 'auth_admin_1f0b668cc21f40c283ffa189a1420309';
DELETE FROM "ApplicationUserAccounts"
 WHERE "Id" = 'auth_admin_1f0b668cc21f40c283ffa189a1420309';

-- 2. Fix NormalizedEmail on the new admin (manwara575) so app lookups
--    (AuthEmailAddress.NormalizeOrThrow produces UPPER) actually find it.
UPDATE "ApplicationUserAccounts"
   SET "NormalizedEmail" = UPPER("Email"),
       "UpdatedAt" = NOW()
 WHERE "Id" = 'auth_admin_c585488a3619292b52f51eac3bcb78fb'
   AND "NormalizedEmail" <> UPPER("Email");

-- 3. Delete old master.admin@oetwithdrhesham.co.uk.
DELETE FROM "RefreshTokenRecords"
 WHERE "ApplicationUserAccountId" = 'auth_admin_146163e5a8f5457b9c6d3';
DELETE FROM "EmailOtpChallenges"
 WHERE "ApplicationUserAccountId" = 'auth_admin_146163e5a8f5457b9c6d3';
DELETE FROM "ApplicationUserAccounts"
 WHERE "Id" = 'auth_admin_146163e5a8f5457b9c6d3';

-- 4. Audit trail.
INSERT INTO "AuditEvents"
    ("Id", "OccurredAt", "ActorId", "ActorName",
     "Action", "ResourceType", "ResourceId", "Details")
VALUES
    (replace(gen_random_uuid()::text, '-', ''), NOW(),
     'root@vps-manual', 'Manual recovery',
     'AdminEmailNormalizationFixed', 'ApplicationUserAccount',
     'auth_admin_c585488a3619292b52f51eac3bcb78fb',
     'Fixed NormalizedEmail to UPPER to match AuthEmailAddress.NormalizeOrThrow.'),
    (replace(gen_random_uuid()::text, '-', ''), NOW(),
     'root@vps-manual', 'Manual recovery',
     'DuplicateAccountRemoved', 'ApplicationUserAccount',
     'auth_admin_1f0b668cc21f40c283ffa189a1420309',
     'Removed broken duplicate mindreader420123 row. Real learner row auth_aac85e6deda340059cec27c01cdc8bc6 preserved.'),
    (replace(gen_random_uuid()::text, '-', ''), NOW(),
     'root@vps-manual', 'Manual recovery',
     'OldAdminRemoved', 'ApplicationUserAccount',
     'auth_admin_146163e5a8f5457b9c6d3',
     'Deleted master.admin@oetwithdrhesham.co.uk after manwara575 admin established.');

COMMIT;

\echo ''
\echo '=== FINAL ADMIN ROSTER ==='
SELECT "Id", "Email", "NormalizedEmail", "Role"
  FROM "ApplicationUserAccounts"
 WHERE "Role" = 'admin'
 ORDER BY "CreatedAt";

\echo ''
\echo '=== mindreader420123 final state (should be exactly one learner row) ==='
SELECT "Id", "Email", "NormalizedEmail", "Role"
  FROM "ApplicationUserAccounts"
 WHERE "NormalizedEmail" = 'MINDREADER420123@GMAIL.COM';

\echo ''
\echo '=== Total account counts by role ==='
SELECT "Role", COUNT(*) FROM "ApplicationUserAccounts" GROUP BY "Role" ORDER BY "Role";
SQL
