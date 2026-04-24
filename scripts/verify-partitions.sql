\d+ "AnalyticsEvents"
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 12;
SELECT n.nspname||'.'||c.relname AS parent, p.relname AS partition
FROM pg_inherits i
JOIN pg_class c ON c.oid=i.inhparent
JOIN pg_class p ON p.oid=i.inhrelid
JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE c.relname IN ('AnalyticsEvents','AuditEvents','AiUsageRecords')
ORDER BY 1,2;
SELECT proname FROM pg_proc WHERE proname='ensure_monthly_partition';
