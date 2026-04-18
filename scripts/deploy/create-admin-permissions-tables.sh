#!/usr/bin/env bash
# Fix the missing admin-permissions tables:
#   - AdminPermissionGrants (referenced on every admin sign-in → causing
#     the 500 "unexpected server error" when manwara575 tries to log in)
#   - AdminUsers
#   - PermissionTemplates
#
# Also grants all 16 admin permissions to manwara575@gmail.com so the
# new admin has full access equivalent to the old master.admin.
#
# This mirrors what migration AddAdminRoleBasedAccessControl would have
# done if someone had generated it. A proper migration file should be
# added in a follow-up PR so future deploys don't hit this gap.
set -euo pipefail
cd /root/oetwebsite
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

MANWARA_ID='auth_admin_c585488a3619292b52f51eac3bcb78fb'

docker exec -i oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" <<SQL
BEGIN;

-- AdminPermissionGrants
CREATE TABLE IF NOT EXISTS "AdminPermissionGrants" (
    "Id"           character varying(64)        NOT NULL,
    "AdminUserId"  character varying(64)        NOT NULL,
    "Permission"   character varying(64)        NOT NULL,
    "GrantedBy"    character varying(128)       NOT NULL,
    "GrantedAt"    timestamp with time zone     NOT NULL,
    CONSTRAINT "PK_AdminPermissionGrants"
        PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AdminPermissionGrants_ApplicationUserAccounts_AdminUserId"
        FOREIGN KEY ("AdminUserId")
        REFERENCES "ApplicationUserAccounts" ("Id")
        ON DELETE CASCADE
);

-- Unique (AdminUserId, Permission) so the same permission can't be granted
-- twice to the same admin (matches the conventions used elsewhere).
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AdminPermissionGrants_AdminUserId_Permission"
    ON "AdminPermissionGrants" ("AdminUserId", "Permission");

-- AdminUsers
CREATE TABLE IF NOT EXISTS "AdminUsers" (
    "Id"           character varying(64)        NOT NULL,
    "DisplayName"  character varying(256)       NOT NULL,
    "Email"        character varying(256)       NOT NULL,
    "Role"         character varying(64)        NOT NULL DEFAULT 'unassigned',
    "IsActive"     boolean                      NOT NULL DEFAULT true,
    "CreatedAt"    timestamp with time zone     NOT NULL,
    CONSTRAINT "PK_AdminUsers" PRIMARY KEY ("Id")
);

-- PermissionTemplates
CREATE TABLE IF NOT EXISTS "PermissionTemplates" (
    "Id"           character varying(64)        NOT NULL,
    "Name"         character varying(128)       NOT NULL,
    "Description"  character varying(512),
    "Permissions"  text                         NOT NULL DEFAULT '[]',
    "CreatedBy"    character varying(128)       NOT NULL,
    "CreatedAt"    timestamp with time zone     NOT NULL,
    CONSTRAINT "PK_PermissionTemplates" PRIMARY KEY ("Id")
);

-- Grant all 16 admin permissions to manwara575 so they have full access
-- equivalent to what master.admin had. WHERE NOT EXISTS → idempotent.
INSERT INTO "AdminPermissionGrants"
    ("Id", "AdminUserId", "Permission", "GrantedBy", "GrantedAt")
SELECT
    replace(gen_random_uuid()::text, '-', ''),
    '$MANWARA_ID',
    perm,
    'root@vps-manual',
    NOW()
  FROM unnest(ARRAY[
    'content:read',
    'content:write',
    'content:publish',
    'content:editor_review',
    'content:publisher_approval',
    'billing:read',
    'billing:write',
    'users:read',
    'users:write',
    'review_ops',
    'quality_analytics',
    'ai_config',
    'feature_flags',
    'audit_logs',
    'system_admin',
    'manage_permissions'
  ]) AS perm
 WHERE NOT EXISTS (
    SELECT 1 FROM "AdminPermissionGrants"
     WHERE "AdminUserId" = '$MANWARA_ID' AND "Permission" = perm
 );

-- Also record the equivalent row in AdminUsers so lookups there don't
-- return null for the new admin.
INSERT INTO "AdminUsers"
    ("Id", "DisplayName", "Email", "Role", "IsActive", "CreatedAt")
VALUES
    ('$MANWARA_ID', 'Master Admin', 'manwara575@gmail.com', 'system_admin', true, NOW())
ON CONFLICT ("Id") DO NOTHING;

-- Audit
INSERT INTO "AuditEvents"
    ("Id", "OccurredAt", "ActorId", "ActorName",
     "Action", "ResourceType", "ResourceId", "Details")
VALUES
    (replace(gen_random_uuid()::text, '-', ''), NOW(),
     'root@vps-manual', 'Manual recovery',
     'SchemaGapRepaired', 'Database',
     'AdminPermissionGrants|AdminUsers|PermissionTemplates',
     'Created the three admin-permission tables that were declared in DbContext but had no migration. Seeded 16 permissions for manwara575.');

COMMIT;

\echo ''
\echo '=== Tables now exist? ==='
SELECT tablename FROM pg_tables
 WHERE schemaname='public'
   AND tablename IN ('AdminPermissionGrants', 'AdminUsers', 'PermissionTemplates')
 ORDER BY tablename;

\echo ''
\echo '=== Permissions granted to manwara575 ==='
SELECT "Permission", "GrantedAt"
  FROM "AdminPermissionGrants"
 WHERE "AdminUserId" = '$MANWARA_ID'
 ORDER BY "Permission";
SQL
