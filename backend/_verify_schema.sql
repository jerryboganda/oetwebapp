SELECT 'migrations' AS k, count(*)::text AS v FROM "__EFMigrationsHistory"
UNION ALL
SELECT 'tables', count(*)::text FROM information_schema.tables WHERE table_schema='public'
UNION ALL
SELECT 'head', max("MigrationId") FROM "__EFMigrationsHistory"
UNION ALL
SELECT 'WritingTutorReviews', (to_regclass('public."WritingTutorReviews"') IS NOT NULL)::text
UNION ALL
SELECT 'AdminPermissionGrants', (to_regclass('public."AdminPermissionGrants"') IS NOT NULL)::text
UNION ALL
SELECT 'ApplicationUserAccounts', (to_regclass('public."ApplicationUserAccounts"') IS NOT NULL)::text;
