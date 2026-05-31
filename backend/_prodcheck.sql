SELECT 'prod_migration_count' AS k, count(*)::text AS v FROM "__EFMigrationsHistory";
SELECT 'prod_head' AS k, "MigrationId" AS v FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1;
SELECT 'prod_WritingTutorReviews' AS k, (to_regclass('public."WritingTutorReviews"') IS NOT NULL)::text AS v;
SELECT 'prod_total_tables' AS k, count(*)::text AS v FROM information_schema.tables WHERE table_schema='public';
