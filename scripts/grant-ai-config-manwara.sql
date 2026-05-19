-- Grant ai_config admin permission to manwara575@gmail.com (idempotent).
-- Then verify and show user's current grants.

INSERT INTO "AdminPermissionGrants" ("Id", "AdminUserId", "Permission", "GrantedBy", "GrantedAt")
SELECT
    'APG-' || REPLACE(gen_random_uuid()::text, '-', ''),
    a."Id",
    'ai_config',
    'system',
    NOW()
FROM "ApplicationUserAccounts" a
WHERE a."NormalizedEmail" = UPPER('manwara575@gmail.com')
  AND a."Role" = 'admin'
  AND NOT EXISTS (
      SELECT 1 FROM "AdminPermissionGrants" g
      WHERE g."AdminUserId" = a."Id" AND g."Permission" = 'ai_config'
  );

SELECT a."Email", a."Role", g."Permission", g."GrantedAt"
FROM "ApplicationUserAccounts" a
LEFT JOIN "AdminPermissionGrants" g ON g."AdminUserId" = a."Id"
WHERE a."NormalizedEmail" = UPPER('manwara575@gmail.com')
ORDER BY g."Permission";
